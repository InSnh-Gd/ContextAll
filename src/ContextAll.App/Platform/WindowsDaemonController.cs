using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using ContextAll.Ipc;
using Microsoft.Win32;

namespace ContextAll.App.Platform;

/// <summary>Starts ContextAll.Host.exe serve on Windows and waits for the per-user named pipe.</summary>
public sealed class WindowsDaemonController : IDaemonController
{
    public bool IsRunningFromAppImage => false;

    public async Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default)
    {
        var launch = ResolveHostExecutable();
        if (launch is null)
        {
            return new DaemonStartResult(DaemonStartOutcome.Failed, "未找到可用的 ContextAll.Host 可执行文件。");
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

            var pipeName = @"ContextAll\" + LocalChannelClient.CurrentUserSid();
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

                if (await CanConnectPipeAsync(pipeName, ct).ConfigureAwait(false))
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

    public Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var launch = ResolveHostExecutable();
        if (launch is null)
        {
            return Task.FromResult(new DaemonActionResult(false, "未找到可用于开机自启的 ContextAll.Host 可执行文件。"));
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new DaemonActionResult(false, "开机自启仅在 Windows 上可用。"));
        }

        try
        {
            WriteCurrentUserRun(launch);
            return Task.FromResult(new DaemonActionResult(true, "已写入当前用户开机自启。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DaemonActionResult(false, ex.Message));
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteCurrentUserRun(string launch)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key is null)
        {
            throw new InvalidOperationException("无法写入当前用户的 Run 注册表项。");
        }

        key.SetValue("ContextAll", $"\"{launch}\" serve");
    }

    public async Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default)
    {
        var launch = ResolveHostExecutable();
        if (launch is null)
        {
            return new DaemonActionResult(false, "未找到可用于重置身份的 ContextAll.Host 可执行文件。");
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

    public static string? ResolveHostExecutable() => ResolveHostExecutable(AppContext.BaseDirectory);

    public static string? ResolveHostExecutable(string baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return null;
        }

        foreach (var name in HostFileNames)
        {
            var bundled = Path.Combine(baseDir, name);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir));
        var tfmDir = new DirectoryInfo(trimmed);
        var configDir = tfmDir.Parent;
        var binDir = configDir?.Parent;
        var projectDir = binDir?.Parent;
        var srcDir = projectDir?.Parent;
        if (binDir is { Name: "bin" } && configDir is not null && srcDir is not null)
        {
            foreach (var name in HostFileNames)
            {
                var siblingHost = Path.Combine(
                    srcDir.FullName,
                    "ContextAll.Host",
                    "bin",
                    configDir.Name,
                    tfmDir.Name,
                    name);
                if (File.Exists(siblingHost))
                {
                    return siblingHost;
                }
            }
        }

        return null;
    }

    public static bool HasExistingIdentityOrSettingsOnDisk()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var idDir = Path.Combine(root, "ContextAll", "identity");
        var configFile = Path.Combine(root, "ContextAll", "config.yaml");
        return (Directory.Exists(idDir) && Directory.EnumerateFileSystemEntries(idDir).Any()) || File.Exists(configFile);
    }

    private static readonly string[] HostFileNames = ["ContextAll.Host.exe", "ContextAll.Host"];

    private static async Task<bool> CanConnectPipeAsync(string pipeName, CancellationToken ct)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(200, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
