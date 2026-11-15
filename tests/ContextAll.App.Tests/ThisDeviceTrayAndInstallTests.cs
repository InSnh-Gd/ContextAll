using Avalonia.Controls;
using Avalonia.VisualTree;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.App.Views;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class ThisDeviceTrayAndInstallTests
{
    [Fact]
    public void TrayClosePolicy_exits_when_no_watcher_even_if_tray_preference_is_enabled()
    {
        Assert.False(TrayClosePolicy.ShouldHideToTray(trayEnabledInPreferences: true, watcherAvailable: false));
        Assert.False(TrayClosePolicy.ShouldHideToTray(trayEnabledInPreferences: false, watcherAvailable: true));
        Assert.True(TrayClosePolicy.ShouldHideToTray(trayEnabledInPreferences: true, watcherAvailable: true));
    }

    [Fact]
    public void Disconnected_shell_shows_onboarding_and_never_auto_starts_daemon() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var daemon = new FakeDaemonController();
            var saved = new List<ShellPreferences>();

            var vm = new ShellViewModel(client, daemon, new ShellPreferences(), saved.Add);
            var root = new ShellRootView(vm);

            Assert.False(vm.Connected);
            Assert.True(vm.ShowDaemonOnboarding);
            Assert.True(root.IsOnboardingVisible);
            Assert.Equal(0, daemon.StartCalls);

            // Once daemon starts externally, onboarding hides automatically
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalDisplayName = "我的 Linux 电脑",
            });

            Assert.True(vm.Connected);
            Assert.False(vm.ShowDaemonOnboarding);
            Assert.False(root.IsOnboardingVisible);
            Assert.Equal(0, daemon.StartCalls);
        });

    [Fact]
    public async Task AppImageInstaller_writes_binary_systemd_unit_and_desktop_entry_into_temp_home()
    {
        var tempRoot = Directory.CreateTempSubdirectory("ca-appimg-install-");
        try
        {
            var fakeAppImage = Path.Combine(tempRoot.FullName, "source.AppImage");
            await File.WriteAllTextAsync(fakeAppImage, "fake-appimage-payload");

            var homeDir = Path.Combine(tempRoot.FullName, "home");
            Directory.CreateDirectory(homeDir);

            var systemctlCalls = new List<string[]>();
            var result = await AppImageInstaller.InstallAsync(
                fakeAppImage,
                homeDir,
                xdgConfigHome: null,
                xdgDataHome: null,
                systemctlRunner: (args, _) =>
                {
                    systemctlCalls.Add(args);
                    return Task.FromResult(0);
                });

            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(result.InstalledBinaryPath));
            Assert.Equal("fake-appimage-payload", await File.ReadAllTextAsync(result.InstalledBinaryPath));

            if (OperatingSystem.IsLinux())
            {
                var mode = File.GetUnixFileMode(result.InstalledBinaryPath);
                Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
            }

            Assert.True(File.Exists(result.ServiceUnitPath));
            var unit = await File.ReadAllTextAsync(result.ServiceUnitPath);
            Assert.Contains("Type=notify", unit);
            Assert.Contains("ExecStart=%h/.local/bin/ContextAll.AppImage serve", unit);
            Assert.Contains("WantedBy=default.target", unit);

            Assert.True(File.Exists(result.DesktopEntryPath));
            var desktop = await File.ReadAllTextAsync(result.DesktopEntryPath);
            Assert.Contains(result.InstalledBinaryPath, desktop);
            Assert.Contains("Name=ContextAll", desktop);

            Assert.Equal(2, systemctlCalls.Count);
            Assert.Equal(["--user", "daemon-reload"], systemctlCalls[0]);
            Assert.Equal(["--user", "enable", "--now", "contextall.service"], systemctlCalls[1]);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public Task Connected_ThisDevice_section_exposes_zero_reset_controls_and_persists_preferences() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalDeviceId = "DEV-LOCAL-1234",
                LocalDisplayName = "Workstation",
                DownloadDirectory = "/home/user/Downloads",
                ResolvedDownloadDirectory = "/home/user/Downloads",
            });

            var daemon = new FakeDaemonController { IsRunningFromAppImage = true };
            var saved = new List<ShellPreferences>();
            var shellVm = new ShellViewModel(client, daemon, new ShellPreferences(), saved.Add);
            var view = new ThisDeviceView(shellVm.ThisDevice);

            // Zero reset members or UI strings anywhere in ThisDevice while connected
            var publicMemberNames = typeof(ThisDeviceViewModel)
                .GetMembers()
                .Select(m => m.Name)
                .ToArray();
            Assert.DoesNotContain(publicMemberNames, n => n.Contains("Reset", StringComparison.OrdinalIgnoreCase));

            var allTexts = CollectControlTexts(view);
            Assert.DoesNotContain(allTexts, t => t.Contains("重置", StringComparison.Ordinal));
            Assert.DoesNotContain(allTexts, t => t.Contains("Reset", StringComparison.OrdinalIgnoreCase));

            // Verify saving display name, folder picker, tray & native titlebar preferences
            shellVm.ThisDevice.DisplayNameDraft = "Linux Desk";
            Assert.True(await shellVm.ThisDevice.SaveDisplayNameAsync());
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.SetDisplayName && c.Payload.DisplayName == "Linux Desk");

            shellVm.ThisDevice.FolderPicker = _ => Task.FromResult<string?>("/tmp/custom-dl");
            Assert.True(await shellVm.ThisDevice.ChooseDownloadDirectoryAsync());
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.SetDownloadDirectory && c.Payload.Path == "/tmp/custom-dl");

            shellVm.ThisDevice.SetTrayEnabled(true);
            shellVm.ThisDevice.SetUseNativeTitleBar(true);
            Assert.Equal(2, saved.Count);
            Assert.True(saved[^1].TrayEnabled);
            Assert.True(saved[^1].UseNativeTitleBar);
        });

    [Fact]
    public Task Corrupt_state_on_explicit_start_prompts_cancel_by_default_and_resets_only_when_confirmed() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var daemon = new FakeDaemonController
            {
                NextStartResult = new DaemonStartResult(DaemonStartOutcome.CorruptState, "身份文件已损坏"),
            };
            var vm = new ShellViewModel(client, daemon, new ShellPreferences(), _ => { });
            var root = new ShellRootView(vm);

            // Explicit start encounters corrupt state -> opens Reset/Cancel dialog defaulting to cancel
            Assert.False(await vm.StartDaemonExplicitAsync());
            Assert.True(vm.IsCorruptStateDialogOpen);
            Assert.True(root.IsCorruptModalVisible);
            Assert.Equal("cancel", vm.DefaultCorruptAction);
            Assert.Equal(0, daemon.ResetCalls);

            // Canceling closes dialog cleanly with zero reset calls
            vm.CancelCorruptReset();
            Assert.False(vm.IsCorruptStateDialogOpen);
            Assert.False(root.IsCorruptModalVisible);
            Assert.Equal(0, daemon.ResetCalls);

            // Retry start -> corrupt -> confirm reset -> resets then starts daemon -> RetryNow
            Assert.False(await vm.StartDaemonExplicitAsync());
            Assert.True(vm.IsCorruptStateDialogOpen);

            daemon.NextStartResult = new DaemonStartResult(DaemonStartOutcome.Started);
            Assert.True(await vm.ConfirmResetAndStartAsync());
            Assert.Equal(1, daemon.ResetCalls);
            Assert.Equal(3, daemon.StartCalls);
            Assert.False(vm.IsCorruptStateDialogOpen);
            Assert.Equal(1, client.RetryCount);
        });

    private static List<string> CollectControlTexts(Control root)
    {
        var texts = new List<string>();
        var queue = new Queue<object>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            switch (current)
            {
                case TextBlock tb when tb.Text is not null:
                    texts.Add(tb.Text);
                    break;
                case ContentControl cc:
                    if (cc.Content is string s) texts.Add(s);
                    else if (cc.Content is not null) queue.Enqueue(cc.Content);
                    break;
                case Decorator dec when dec.Child is not null:
                    queue.Enqueue(dec.Child);
                    break;
                case Panel panel:
                    foreach (var child in panel.Children) queue.Enqueue(child);
                    break;
            }
        }

        return texts;
    }

    [Fact]
    public void DaemonLauncher_resolves_sibling_host_binary_in_developer_bin_layout()
    {
        var tempRoot = Directory.CreateTempSubdirectory("ca-host-resolve-");
        try
        {
            var appBin = Path.Combine(tempRoot.FullName, "src", "ContextAll.App.Desktop", "bin", "Release", "net10.0");
            var hostBin = Path.Combine(tempRoot.FullName, "src", "ContextAll.Host", "bin", "Release", "net10.0");
            Directory.CreateDirectory(appBin);
            Directory.CreateDirectory(hostBin);
            var expectedHost = Path.Combine(hostBin, "ContextAll.Host");
            File.WriteAllText(expectedHost, "stub");

            var resolved = LinuxDaemonController.ResolveHostExecutable(appBin, activeAppImagePath: null);
            Assert.Equal(expectedHost, resolved);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    private sealed class FakeDaemonController : IDaemonController
    {
        public bool IsRunningFromAppImage { get; set; }
        public int StartCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public DaemonStartResult NextStartResult { get; set; } = new(DaemonStartOutcome.Started);

        public Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(NextStartResult);
        }

        public Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true, "ok"));

        public Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default)
        {
            ResetCalls++;
            return Task.FromResult(new DaemonActionResult(true));
        }
    }
}
