using System.Net.Sockets;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Tests;

public sealed class ShellStartupAndPreferencesTests
{
    [Fact]
    public void LinuxDisplaySelector_prefers_wayland_then_x11_then_none()
    {
        var waylandAndX11 = LinuxDisplaySelector.Select("wayland-0", ":0");
        Assert.Equal(LinuxDisplayBackend.Wayland, waylandAndX11.Backend);
        Assert.Null(waylandAndX11.ErrorMessage);

        var waylandOnly = LinuxDisplaySelector.Select("wayland-1", null);
        Assert.Equal(LinuxDisplayBackend.Wayland, waylandOnly.Backend);

        var x11Only = LinuxDisplaySelector.Select("", ":0");
        Assert.Equal(LinuxDisplayBackend.X11, x11Only.Backend);
        Assert.Null(x11Only.ErrorMessage);

        var neither = LinuxDisplaySelector.Select(null, "  ");
        Assert.Equal(LinuxDisplayBackend.None, neither.Backend);
        Assert.False(string.IsNullOrWhiteSpace(neither.ErrorMessage));
    }

    [Fact]
    public async Task SingleInstanceShell_activates_existing_instance_and_recovers_stale_socket()
    {
        var tempRoot = Directory.CreateTempSubdirectory("ca-").FullName;
        try
        {
            var socketPath = SingleInstanceShell.SocketPath(tempRoot);
            var activations = new List<string?>();
            var activatedSignal = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = await SingleInstanceShell.TryAcquireOrActivateAsync(
                socketPath,
                null,
                token =>
                {
                    activations.Add(token);
                    activatedSignal.TrySetResult(token);
                });
            Assert.True(first.Acquired);
            Assert.NotNull(first.Instance);
            Assert.True(File.Exists(socketPath));

            if (OperatingSystem.IsLinux())
            {
                var mode = File.GetUnixFileMode(socketPath);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            }

            var second = await SingleInstanceShell.TryAcquireOrActivateAsync(
                socketPath,
                "xdg-token-42",
                _ => throw new InvalidOperationException("Second instance must not become listener"));
            Assert.False(second.Acquired);
            Assert.Null(second.Instance);

            var receivedToken = await activatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("xdg-token-42", receivedToken);
            Assert.Single(activations);

            await first.Instance.DisposeAsync();
            Assert.False(File.Exists(socketPath));

            // Simulate a stale leftover socket file from a crashed process (e.g. non-listening/unlinked crash artifact).
            var staleDir = Path.GetDirectoryName(socketPath) ?? tempRoot;
            Directory.CreateDirectory(staleDir);
            using var deadSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            deadSocket.Bind(new UnixDomainSocketEndPoint(socketPath));

            Assert.True(Path.Exists(socketPath));
            var recovered = await SingleInstanceShell.TryAcquireOrActivateAsync(socketPath, null, _ => { });
            Assert.True(recovered.Acquired);
            Assert.NotNull(recovered.Instance);
            await recovered.Instance.DisposeAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void ShellPreferencesStore_persists_shell_json_atomically_and_leaves_config_yaml_untouched()
    {
        var tempConfigHome = Path.Combine(Path.GetTempPath(), "contextall-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempConfigHome);
        try
        {
            var appConfigDir = Path.Combine(tempConfigHome, "contextall");
            Directory.CreateDirectory(appConfigDir);
            var daemonConfigPath = Path.Combine(appConfigDir, "config.yaml");
            const string daemonYaml = "displayName: DaemonNode\ndownloadDirectory: /srv/downloads\ntrust: []\n";
            File.WriteAllText(daemonConfigPath, daemonYaml);

            var shellJsonPath = ShellPreferencesStore.PreferencesPath(tempConfigHome);
            Assert.Equal(Path.Combine(appConfigDir, "shell.json"), shellJsonPath);

            var defaults = ShellPreferencesStore.Load(shellJsonPath);
            Assert.False(defaults.TrayEnabled);
            Assert.False(defaults.UseNativeTitleBar);
            Assert.Equal(980, defaults.WindowWidth);
            Assert.Equal(680, defaults.WindowHeight);
            Assert.False(defaults.GuideSeen);

            var updated = new ShellPreferences(
                TrayEnabled: true,
                UseNativeTitleBar: true,
                WindowWidth: 1120,
                WindowHeight: 740,
                GuideSeen: true);
            ShellPreferencesStore.Save(shellJsonPath, updated);

            var loaded = ShellPreferencesStore.Load(shellJsonPath);
            Assert.Equal(updated, loaded);
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(shellJsonPath));
            }

            // config.yaml must remain completely untouched by UI preferences persistence.
            Assert.Equal(daemonYaml, File.ReadAllText(daemonConfigPath));

            // Corrupt JSON falls back cleanly to safe defaults.
            File.WriteAllText(shellJsonPath, "{ not-valid-json");
            var recovered = ShellPreferencesStore.Load(shellJsonPath);
            Assert.Equal(new ShellPreferences(), recovered);
        }
        finally
        {
            try
            {
                Directory.Delete(tempConfigHome, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void ShellPreferencesStore_supports_nullable_appearance_overrides_and_legacy_json()
    {
        var tempConfigHome = Path.Combine(Path.GetTempPath(), "contextall-appearance-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempConfigHome);
        try
        {
            var shellJsonPath = ShellPreferencesStore.PreferencesPath(tempConfigHome);
            var shellDir = Path.GetDirectoryName(shellJsonPath) ?? tempConfigHome;
            Directory.CreateDirectory(shellDir);

            // Legacy shell.json without appearance override fields must load cleanly with null overrides.
            File.WriteAllText(shellJsonPath, """
            {
              "trayEnabled": true,
              "useNativeTitleBar": false,
              "windowWidth": 1024,
              "windowHeight": 720,
              "guideSeen": true
            }
            """);

            var legacy = ShellPreferencesStore.Load(shellJsonPath);
            Assert.True(legacy.TrayEnabled);
            Assert.Null(legacy.AppearanceModeOverride);
            Assert.Null(legacy.AccentSourceOverride);
            Assert.Equal(AppearanceModeOverride.FollowSystem, legacy.EffectiveAppearanceMode);
            Assert.Equal(AccentSourceOverride.FollowSystem, legacy.EffectiveAccentSource);

            var updated = legacy with
            {
                AppearanceModeOverride = AppearanceModeOverride.Dark,
                AccentSourceOverride = AccentSourceOverride.ForestTeal,
            };
            ShellPreferencesStore.Save(shellJsonPath, updated);

            var reloaded = ShellPreferencesStore.Load(shellJsonPath);
            Assert.Equal(AppearanceModeOverride.Dark, reloaded.AppearanceModeOverride);
            Assert.Equal(AccentSourceOverride.ForestTeal, reloaded.AccentSourceOverride);
            Assert.Equal(AppearanceModeOverride.Dark, reloaded.EffectiveAppearanceMode);
            Assert.Equal(AccentSourceOverride.ForestTeal, reloaded.EffectiveAccentSource);
        }
        finally
        {
            try
            {
                Directory.Delete(tempConfigHome, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void ThisDeviceViewModel_persists_appearance_mode_and_accent_overrides_through_preferences()
    {
        var saved = new List<ShellPreferences>();
        var vm = new ThisDeviceViewModel(
            new FakeLocalDaemonClient(),
            new StubDaemonController(),
            new ShellPreferences(),
            saved.Add);

        Assert.Equal(AppearanceModeOverride.FollowSystem, vm.AppearanceModeOverride);
        Assert.Equal(AccentSourceOverride.FollowSystem, vm.AccentSourceOverride);

        vm.SetAppearanceModeOverride(AppearanceModeOverride.Dark);
        vm.SetAccentSourceOverride(AccentSourceOverride.SunsetTerracotta);

        Assert.Equal(AppearanceModeOverride.Dark, vm.AppearanceModeOverride);
        Assert.Equal(AccentSourceOverride.SunsetTerracotta, vm.AccentSourceOverride);
        Assert.Equal(2, saved.Count);
        Assert.Equal(AppearanceModeOverride.Dark, saved[^1].AppearanceModeOverride);
        Assert.Equal(AccentSourceOverride.SunsetTerracotta, saved[^1].AccentSourceOverride);
    }

    private sealed class StubDaemonController : IDaemonController
    {
        public bool IsRunningFromAppImage => false;
        public Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonStartResult(DaemonStartOutcome.Started));
        public Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true));
        public Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true));
    }
}
