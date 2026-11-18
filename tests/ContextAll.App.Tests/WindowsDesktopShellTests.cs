using Avalonia.Media;
using Avalonia.Styling;
using ContextAll.App.Platform;
using ContextAll.App.Theming;
using ContextAll.App.ViewModels;

namespace ContextAll.App.Tests;

public sealed class WindowsDesktopShellTests
{
    [Fact]
    public void Windows_shell_json_lives_under_local_app_data_contextall()
    {
        var path = ShellPreferencesStore.WindowsPreferencesPath(@"C:\Users\ada\AppData\Local");
        Assert.Equal(Path.Combine(@"C:\Users\ada\AppData\Local", "ContextAll", "shell.json"), path);
        Assert.DoesNotContain(".config", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Windows_tray_probe_is_always_available()
    {
        var probe = new AlwaysAvailableStatusNotifierProbe();
        Assert.True(await probe.IsWatcherAvailableAsync());
        Assert.True(TrayClosePolicy.ShouldHideToTray(trayEnabledInPreferences: true, watcherAvailable: true));
    }

    [Fact]
    public void Windows_appearance_maps_apps_dark_to_dark_variant_and_keeps_segoe_fonts()
    {
        var dark = WindowsAppearanceSource.Resolve(appsUseLightTheme: false);
        Assert.Equal(ThemeVariant.Dark, dark.Variant);
        Assert.Contains("Segoe UI", dark.UiFontFamily, StringComparison.Ordinal);
        Assert.Contains("Cascadia Mono", dark.MonospaceFontFamily, StringComparison.Ordinal);

        var accent = Color.FromRgb(0x00, 0x78, 0xD4);
        var light = WindowsAppearanceSource.Resolve(appsUseLightTheme: true, accentColor: accent);
        Assert.Equal(ThemeVariant.Light, light.Variant);
        Assert.Equal(accent, light.AccentSeed);
    }

    [Fact]
    public void Windows_daemon_controller_resolves_sibling_host_exe_in_developer_bin_layout()
    {
        var tempRoot = Directory.CreateTempSubdirectory("ca-win-host-resolve-");
        try
        {
            var appBin = Path.Combine(tempRoot.FullName, "src", "ContextAll.App.Windows", "bin", "Release", "net10.0");
            var hostBin = Path.Combine(tempRoot.FullName, "src", "ContextAll.Host", "bin", "Release", "net10.0");
            Directory.CreateDirectory(appBin);
            Directory.CreateDirectory(hostBin);
            var expectedHost = Path.Combine(hostBin, "ContextAll.Host.exe");
            File.WriteAllText(expectedHost, "stub");

            var resolved = WindowsDaemonController.ResolveHostExecutable(appBin);
            Assert.Equal(expectedHost, resolved);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Windows_single_instance_second_launch_activates_existing()
    {
        var id = Guid.NewGuid().ToString("N");
        var mutexName = "ContextAll-shell-test-" + id;
        var pipeName = "contextall-shell-test-" + id;
        var activations = new List<string?>();
        var activated = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = await PipeSingleInstance.TryAcquireOrActivateAsync(
            mutexName,
            pipeName,
            activationToken: null,
            token =>
            {
                activations.Add(token);
                activated.TrySetResult(token);
            });
        Assert.True(first.Acquired);
        Assert.NotNull(first.Instance);

        try
        {
            var second = await PipeSingleInstance.TryAcquireOrActivateAsync(
                mutexName,
                pipeName,
                activationToken: "win-token-7",
                _ => throw new InvalidOperationException("Second instance must not become listener"));
            Assert.False(second.Acquired);
            Assert.Null(second.Instance);

            var received = await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("win-token-7", received);
            Assert.Single(activations);
        }
        finally
        {
            if (first.Instance is not null)
            {
                await first.Instance.DisposeAsync();
            }
        }
    }

    [Fact]
    public void Windows_desktop_platform_prefers_native_title_bar_and_win32_label()
    {
        var platform = WindowsDesktopPlatform.Instance;
        Assert.True(platform.PreferNativeTitleBar);
        Assert.Equal("Win32", platform.DisplayServerLabel);
        Assert.IsType<AlwaysAvailableStatusNotifierProbe>(platform.CreateTrayProbe());
        Assert.IsType<WindowsDaemonController>(platform.CreateDaemonController());
        Assert.Contains("ContextAll", platform.ResolvePreferencesPath(@"D:\AppData"), StringComparison.Ordinal);
    }

    [Fact]
    public void Display_server_label_is_win32_when_windows()
    {
        Assert.Equal("Win32", ThisDeviceViewModel.ResolveDisplayServerLabel(null, null, isWindows: true));
        Assert.Equal("Wayland (wayland-0)", ThisDeviceViewModel.ResolveDisplayServerLabel("wayland-0", ":0", isWindows: false));
    }
}
