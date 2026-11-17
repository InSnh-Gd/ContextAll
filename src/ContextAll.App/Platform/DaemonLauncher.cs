using System.Diagnostics;
using System.Net.Sockets;
using ContextAll.Ipc;

namespace ContextAll.App.Platform;

/// <summary>Result categories when the operator explicitly starts the background daemon.</summary>
public enum DaemonStartOutcome
{
    Started,
    CorruptState,
    Failed,
}

public sealed record DaemonStartResult(DaemonStartOutcome Outcome, string? Message = null);

public sealed record DaemonActionResult(bool Succeeded, string? Message = null);

/// <summary>Abstraction for explicit user-initiated daemon start, autostart registration, and corrupt-state reset.</summary>
public interface IDaemonController
{
    bool IsRunningFromAppImage { get; }

    Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default);

    Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default);

    Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default);
}

/// <summary>Controls the Linux contextall-host / AppImage daemon explicitly without auto-starting on shell launch.</summary>
public sealed class LinuxDaemonController : IDaemonController
{
    public bool IsRunningFromAppImage => AppImageInstaller.ResolveActiveAppImagePath() is not null;

    public async Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default)
    {
        var launch = ResolveHostExecutable();
        if (launch is null)
        {
            return new DaemonStartResult(DaemonStartOutcome.Failed, "未找到可用的 contextall-host 或 AppImage 可执行文件。");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launch,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("serve");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new DaemonStartResult(DaemonStartOutcome.Failed, "无法启动后台服务进程。");
            }

            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var socketPath = string.IsNullOrWhiteSpace(runtimeDir) ? null : LocalChannelClient.LinuxSocket(runtimeDir);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    if (process.ExitCode != 0 && HasExistingIdentityOrSettingsOnDisk())
                    {
                        return new DaemonStartResult(
                            DaemonStartOutcome.CorruptState,
                            "后台服务启动失败：本机身份凭证或配置文件可能已损坏。");
                    }

                    return new DaemonStartResult(
                        DaemonStartOutcome.Failed,
                        $"后台服务异常退出（退出码 {process.ExitCode}）。");
                }

                if (socketPath is not null && await CanConnectUnixSocketAsync(socketPath, ct).ConfigureAwait(false))
                {
                    return new DaemonStartResult(DaemonStartOutcome.Started);
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            return new DaemonStartResult(DaemonStartOutcome.Started);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DaemonStartResult(DaemonStartOutcome.Failed, ex.Message);
        }
    }

    public async Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default)
    {
        var appImage = AppImageInstaller.ResolveActiveAppImagePath();
        if (appImage is not null)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var install = await AppImageInstaller.InstallAsync(
                appImage,
                home,
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                ct: ct).ConfigureAwait(false);

            return install.Succeeded
                ? new DaemonActionResult(true, "已安装 AppImage 并启用用户级开机自启。")
                : new DaemonActionResult(false, install.Error ?? "AppImage 自启动安装失败。");
        }

        var reload = await AppImageInstaller.RunSystemctlAsync(["--user", "daemon-reload"], ct).ConfigureAwait(false);
        var enable = await AppImageInstaller.RunSystemctlAsync(["--user", "enable", "--now", AppImageInstaller.ServiceFileName], ct).ConfigureAwait(false);
        return reload == 0 && enable == 0
            ? new DaemonActionResult(true, "已启用 systemd 用户自启动服务。")
            : new DaemonActionResult(false, "未能启用 contextall.service，请确认已安装用户服务单元或通过 AppImage 运行。");
    }

    public async Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default)
    {
        var launch = ResolveHostExecutable();
        if (launch is null)
        {
            return new DaemonActionResult(false, "未找到可用于重置身份的 contextall-host 可执行文件。");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = launch,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("identity");
            psi.ArgumentList.Add("reset");
            psi.ArgumentList.Add("--yes");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new DaemonActionResult(false, "无法执行身份重置命令。");
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0
                ? new DaemonActionResult(true)
                : new DaemonActionResult(false, $"身份重置失败（退出码 {process.ExitCode}）。");
        }
        catch (Exception ex)
        {
            return new DaemonActionResult(false, ex.Message);
        }
    }

    public static string? ResolveHostExecutable() =>
        ResolveHostExecutable(AppContext.BaseDirectory, AppImageInstaller.ResolveActiveAppImagePath());

    public static string? ResolveHostExecutable(string baseDir, string? activeAppImagePath)
    {
        if (!string.IsNullOrWhiteSpace(activeAppImagePath) && File.Exists(activeAppImagePath))
        {
            return activeAppImagePath;
        }

        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return null;
        }

        var bundledHost = Path.Combine(baseDir, "contextall-host");
        if (File.Exists(bundledHost))
        {
            return bundledHost;
        }

        var installedHost = Path.Combine(baseDir, "ContextAll.Host");
        if (File.Exists(installedHost))
        {
            return installedHost;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir));
        var tfmDir = new DirectoryInfo(trimmed);
        var configDir = tfmDir.Parent;
        var binDir = configDir?.Parent;
        var projectDir = binDir?.Parent;
        var srcDir = projectDir?.Parent;
        if (binDir is { Name: "bin" } && configDir is not null && srcDir is not null)
        {
            var siblingHost = Path.Combine(srcDir.FullName, "ContextAll.Host", "bin", configDir.Name, tfmDir.Name, "ContextAll.Host");
            if (File.Exists(siblingHost))
            {
                return siblingHost;
            }
        }

        return null;
    }

    public static bool HasExistingIdentityOrSettingsOnDisk()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataRoot = string.IsNullOrWhiteSpace(dataHome) ? Path.Combine(home, ".local", "share") : dataHome;
        var idDir = Path.Combine(dataRoot, "contextall", "identity");

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = string.IsNullOrWhiteSpace(configHome) ? Path.Combine(home, ".config") : configHome;
        var configFile = Path.Combine(configRoot, "contextall", "config.yaml");

        return (Directory.Exists(idDir) && Directory.EnumerateFileSystemEntries(idDir).Any()) || File.Exists(configFile);
    }

    private static async Task<bool> CanConnectUnixSocketAsync(string socketPath, CancellationToken ct)
    {
        if (!Path.Exists(socketPath))
        {
            return false;
        }

        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await probe.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
