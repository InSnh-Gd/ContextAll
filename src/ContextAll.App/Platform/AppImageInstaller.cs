using System.Diagnostics;

namespace ContextAll.App.Platform;

/// <summary>Result of self-installing an AppImage alongside user systemd and desktop entries.</summary>
public sealed record AppImageInstallResult(
    bool Succeeded,
    string InstalledBinaryPath,
    string ServiceUnitPath,
    string DesktopEntryPath,
    string? Error = null);

/// <summary>Copies the active AppImage into ~/.local/bin and registers user systemd autostart + .desktop entry.</summary>
public static class AppImageInstaller
{
    public const string InstalledFileName = "ContextAll.AppImage";
    public const string ServiceFileName = "contextall.service";
    public const string DesktopFileName = "contextall.desktop";

    public static string? ResolveActiveAppImagePath()
    {
        var envPath = Environment.GetEnvironmentVariable("APPIMAGE");
        return !string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath) ? envPath : null;
    }

    public static async Task<AppImageInstallResult> InstallAsync(
        string sourceAppImagePath,
        string homeDir,
        string? xdgConfigHome = null,
        string? xdgDataHome = null,
        Func<string[], CancellationToken, Task<int>>? systemctlRunner = null,
        CancellationToken ct = default)
    {
        var binDir = Path.Combine(homeDir, ".local", "bin");
        var targetBinary = Path.Combine(binDir, InstalledFileName);
        var configRoot = string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(homeDir, ".config") : xdgConfigHome;
        var dataRoot = string.IsNullOrWhiteSpace(xdgDataHome) ? Path.Combine(homeDir, ".local", "share") : xdgDataHome;

        var serviceDir = Path.Combine(configRoot, "systemd", "user");
        var servicePath = Path.Combine(serviceDir, ServiceFileName);
        var appsDir = Path.Combine(dataRoot, "applications");
        var desktopPath = Path.Combine(appsDir, DesktopFileName);

        if (string.IsNullOrWhiteSpace(sourceAppImagePath) || !File.Exists(sourceAppImagePath))
        {
            return new AppImageInstallResult(false, targetBinary, servicePath, desktopPath, "未找到正在运行的 AppImage 文件路径。");
        }

        try
        {
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(serviceDir);
            Directory.CreateDirectory(appsDir);

            var fullSource = Path.GetFullPath(sourceAppImagePath);
            var fullTarget = Path.GetFullPath(targetBinary);
            if (!string.Equals(fullSource, fullTarget, StringComparison.Ordinal))
            {
                var tempBinary = $"{targetBinary}.{Environment.ProcessId}.tmp";
                await using (var src = File.OpenRead(fullSource))
                await using (var dst = File.Create(tempBinary))
                {
                    await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                }

                File.Move(tempBinary, targetBinary, overwrite: true);
            }

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    targetBinary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var unitContent = string.Join('\n',
                "[Unit]",
                "Description=ContextAll",
                "",
                "[Service]",
                "Type=notify",
                "ExecStart=%h/.local/bin/ContextAll.AppImage serve",
                "",
                "[Install]",
                "WantedBy=default.target",
                "");
            await File.WriteAllTextAsync(servicePath, unitContent, ct).ConfigureAwait(false);

            var desktopContent = string.Join('\n',
                "[Desktop Entry]",
                "Name=ContextAll",
                "Comment=局域网剪贴板同步与文件传输",
                $"Exec=\"{targetBinary}\"",
                "Icon=contextall",
                "Terminal=false",
                "Type=Application",
                "Categories=Network;Utility;",
                "");
            await File.WriteAllTextAsync(desktopPath, desktopContent, ct).ConfigureAwait(false);

            var runner = systemctlRunner ?? RunSystemctlAsync;
            var reloadCode = await runner(["--user", "daemon-reload"], ct).ConfigureAwait(false);
            if (reloadCode != 0)
            {
                return new AppImageInstallResult(false, targetBinary, servicePath, desktopPath, $"systemctl --user daemon-reload 返回 {reloadCode}。");
            }

            var enableCode = await runner(["--user", "enable", "--now", ServiceFileName], ct).ConfigureAwait(false);
            if (enableCode != 0)
            {
                return new AppImageInstallResult(false, targetBinary, servicePath, desktopPath, $"systemctl --user enable 返回 {enableCode}。");
            }

            return new AppImageInstallResult(true, targetBinary, servicePath, desktopPath);
        }
        catch (Exception ex)
        {
            return new AppImageInstallResult(false, targetBinary, servicePath, desktopPath, ex.Message);
        }
    }

    public static async Task<int> RunSystemctlAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "systemctl",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return 1;
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch
        {
            return 1;
        }
    }
}
