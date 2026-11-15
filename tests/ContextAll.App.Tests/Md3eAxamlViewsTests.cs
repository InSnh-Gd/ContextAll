using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ContextAll.App.Controls;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.App.Views;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class Md3eAxamlViewsTests
{
    [Fact]
    public Task ShellRootView_axaml_arranges_wide_and_narrow_chrome_corrupt_dialog_and_snackbar() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var daemon = new FakeDaemonController
            {
                NextStartResult = new DaemonStartResult(DaemonStartOutcome.CorruptState, "配置损坏"),
            };
            var vm = new ShellViewModel(client, daemon, new ShellPreferences(), _ => { });
            var root = new ShellRootView(vm);

            // Wide window (1280x800 >= 840) shows rail, hides bottom floating toolbar
            vm.ApplyWidth(1280);
            root.Measure(new Size(1280, 800));
            root.Arrange(new Rect(0, 0, 1280, 800));
            Assert.True(root.IsRailVisible);
            Assert.False(root.IsDockVisible);

            // Narrow window (520x720 < 840) shows HorizontalFloatingToolbar, hides rail
            vm.ApplyWidth(520);
            root.Measure(new Size(520, 720));
            root.Arrange(new Rect(0, 0, 520, 720));
            Assert.False(root.IsRailVisible);
            Assert.True(root.IsDockVisible);

            // Snackbar integration via ViewModel
            vm.RequestSnackbar("已复制端点地址");
            Assert.True(root.SnackbarHost.IsOpen);
            Assert.Equal("已复制端点地址", root.SnackbarHost.CurrentMessage);

            // Corrupt state opens MaterialDialog defaulting focus to Cancel
            Assert.False(await vm.StartDaemonExplicitAsync());
            Assert.True(root.CorruptDialog.IsOpen);
            Assert.True(root.CorruptDialog.DefaultFocusCancel);
            root.CorruptDialog.InvokeCancel();
            Assert.False(vm.IsCorruptStateDialogOpen);
            Assert.Equal(0, daemon.ResetCalls);
        });

    [Fact]
    public void DevicesView_tracks_live_theme_variant_changes_on_detail_hero_card() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var app = Assert.IsType<App>(Application.Current);
            app.ApplyAppearanceOverrides(AppearanceModeOverride.Light, AccentSourceOverride.BrandBlue);

            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("DEV-T", "测试设备", true, "online", true, true, true, "10.0.0.1:42461");
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [peer],
            });

            var vm = new DevicesViewModel(client);
            vm.SelectDevice("DEV-T");
            var view = new DevicesView(vm);
            var window = new Window { Width = 1100, Height = 720, Content = view };
            window.Measure(new Size(1100, 720));
            window.Arrange(new Rect(0, 0, 1100, 720));

            var heroBefore = view.GetLogicalDescendants()
                .OfType<Border>()
                .First(b => b.CornerRadius == new CornerRadius(24) && b.Background is ISolidColorBrush);
            var lightHeroColor = Assert.IsAssignableFrom<ISolidColorBrush>(heroBefore.Background).Color;

            app.ApplyAppearanceOverrides(AppearanceModeOverride.Dark, AccentSourceOverride.ForestTeal);

            var heroAfter = view.GetLogicalDescendants()
                .OfType<Border>()
                .First(b => b.CornerRadius == new CornerRadius(24) && b.Background is ISolidColorBrush);
            var darkHeroColor = Assert.IsAssignableFrom<ISolidColorBrush>(heroAfter.Background).Color;

            Assert.NotEqual(lightHeroColor, darkHeroColor);

            app.ApplyAppearanceOverrides(AppearanceModeOverride.FollowSystem, AccentSourceOverride.FollowSystem);
        });

    [Fact]
    public void DevicesView_g1_expressive_studio_validates_dual_sheet_empty_state_real_telemetry_and_broadcast_dialog() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var vm = new DevicesViewModel(client);
            var view = new DevicesView(vm);

            // Empty state: shows unified empty panel with ContainedLoadingIndicator, hides section headers
            vm.ApplyWidth(1280);
            view.Measure(new Size(1160, 740));
            view.Arrange(new Rect(0, 0, 1160, 740));
            Assert.True(view.IsEmptyStateVisible);
            Assert.NotNull(view.GetLogicalDescendants().OfType<ContainedLoadingIndicator>().FirstOrDefault());

            // Populate trusted (ClipboardSync = false) + discovered + real transfer history
            var syncOffPeer = new DeviceSnapshotPayload("DEV-A", "工作站", true, "online", false, true, true, "192.168.1.5:42461");
            var discoveredPeer = new DeviceSnapshotPayload("DEV-B", "新手机", false, "online", null, null, null, "192.168.1.9:42461");
            var history = new LocalTransferHistoryEntry("t1", "DEV-A", "工作站", "receive", "notes.txt", 512, "done", DateTimeOffset.UtcNow, "/tmp");

            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [syncOffPeer, discoveredPeer],
                History = [history],
            });

            view.Measure(new Size(1160, 740));
            view.Arrange(new Rect(0, 0, 1160, 740));
            Assert.False(view.IsEmptyStateVisible);
            Assert.True(view.IsListPaneVisible);
            Assert.True(view.IsDetailPaneVisible);

            // Narrow (< 840) drill-in
            vm.ApplyWidth(520);
            Assert.True(view.IsListPaneVisible);
            Assert.False(view.IsDetailPaneVisible);

            vm.SelectDevice("DEV-A");
            Assert.False(view.IsListPaneVisible);
            Assert.True(view.IsDetailPaneVisible);

            view.Measure(new Size(500, 680));
            view.Arrange(new Rect(0, 0, 500, 680));

            var renderedTexts = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .ToArray();

            // Zero fabricated telemetry anywhere in rendered tree
            Assert.DoesNotContain(renderedTexts, t => t.Contains("RTT", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(renderedTexts, t => t.Contains("最近活跃", StringComparison.Ordinal));
            Assert.DoesNotContain(renderedTexts, t => t.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));

            // Real history and endpoint appear
            Assert.Contains(renderedTexts, t => t.Contains("notes.txt", StringComparison.Ordinal));
            Assert.Contains(renderedTexts, t => t.Contains("192.168.1.5:42461", StringComparison.Ordinal));

            // 1.A clipboard broadcast MaterialDialog clarifies broadcast + disabled peer notice
            vm.OpenClipboardModal();
            Assert.True(view.ClipboardDialog.IsOpen);
            Assert.Contains("广播", view.ClipboardDialog.Message);
            Assert.Contains("关闭剪贴板同步", view.ClipboardDialog.Message);
        });

    [Fact]
    public void ConnectionView_axaml_binds_endpoints_backend_and_material_guide_dialog() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalAddresses = ["10.0.0.8"],
                QuicPort = 42461,
                ClipboardBackend = "wayland",
            });

            var guideSeen = false;
            var vm = new ConnectionViewModel(client, seen => guideSeen = seen);
            var view = new ConnectionView(vm);

            view.Measure(new Size(1100, 720));
            view.Arrange(new Rect(0, 0, 1100, 720));
            Assert.Contains("Wayland 原生", view.ClipboardStatusText);

            vm.OpenGuide();
            Assert.True(view.GuideDialog.IsOpen);
            view.GuideDialog.InvokePrimaryAction();
            Assert.False(view.IsGuideVisible);
            Assert.True(guideSeen);
        });

    [Fact]
    public Task TransfersView_g2_split_workbench_switches_wide_split_and_narrow_stack_and_renders_wavy_progress() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("DEV-1", "主力笔记本", true, "online", true, true, true, "192.168.1.20:42461");
            var prompt = new FilePromptSnapshotPayload("t-in-1", "DEV-1", "photo.png", 4096, 30_000);
            var active = new TransferSnapshotPayload("t-act-1", "DEV-1", "send", "movie.mp4", 4, 10, false, 10_000);
            var history = new LocalTransferHistoryEntry("t-old-1", "DEV-1", "主力笔记本", "receive", "doc.pdf", 2048, "done", DateTimeOffset.UtcNow, "/tmp");

            var vm = new TransfersViewModel(client);
            var view = new TransfersView(vm);

            // Wide (>= 840) enables g2-split-workbench dual-sheet split; narrow (< 840) stacks vertically
            view.Measure(new Size(1160, 760));
            view.Arrange(new Rect(0, 0, 1160, 760));
            Assert.True(view.IsSplitLayout);

            view.Measure(new Size(520, 720));
            view.Arrange(new Rect(0, 0, 520, 720));
            Assert.False(view.IsSplitLayout);

            view.Measure(new Size(1160, 760));
            view.Arrange(new Rect(0, 0, 1160, 760));

            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [peer],
                FilePrompts = [prompt],
                ActiveTransfers = [active],
                History = [history],
            });

            Assert.Equal(1, view.RenderedActiveTransferCount);
            Assert.Equal(1, view.RenderedHistoryCount);

            var wavy = Assert.Single(view.GetLogicalDescendants().OfType<LinearWavyProgressIndicator>());
            Assert.Equal(10, wavy.Maximum);
            Assert.Equal(4, wavy.Value);

            var renderedTexts = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .ToArray();
            Assert.Contains(renderedTexts, t => t.Contains("photo.png", StringComparison.Ordinal));
            Assert.Contains(renderedTexts, t => t.Contains("movie.mp4", StringComparison.Ordinal));
            Assert.Contains(renderedTexts, t => t.Contains("doc.pdf", StringComparison.Ordinal));

            Assert.True(await vm.AcceptFileAsync("t-in-1"));
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.AcceptFile && c.Payload.TransferId == "t-in-1");
        });

    [Fact]
    public void ThisDeviceView_axaml_exposes_appearance_and_accent_overrides_with_zero_reset_controls() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalDeviceId = "DEV-LOCAL-01",
                LocalDisplayName = "办公台式机",
                ResolvedDownloadDirectory = "/home/user/Downloads",
            });

            var daemon = new FakeDaemonController();
            var saved = new List<ShellPreferences>();
            var vm = new ThisDeviceViewModel(client, daemon, new ShellPreferences(), saved.Add);
            var view = new ThisDeviceView(vm);

            view.Measure(new Size(960, 760));
            view.Arrange(new Rect(0, 0, 960, 760));

            Assert.Equal(AppearanceModeOverride.FollowSystem, view.SelectedAppearanceMode);
            Assert.Equal(AccentSourceOverride.FollowSystem, view.SelectedAccentSource);

            view.SelectAppearanceMode(AppearanceModeOverride.Dark);
            view.SelectAccentSource(AccentSourceOverride.ForestTeal);

            Assert.Equal(AppearanceModeOverride.Dark, view.SelectedAppearanceMode);
            Assert.Equal(AccentSourceOverride.ForestTeal, view.SelectedAccentSource);
            Assert.True(saved.Count >= 2);
            Assert.Equal(AppearanceModeOverride.Dark, saved[^1].AppearanceModeOverride);
            Assert.Equal(AccentSourceOverride.ForestTeal, saved[^1].AccentSourceOverride);

            var allTexts = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .Concat(view.GetLogicalDescendants().OfType<Button>().Select(b => b.Content?.ToString() ?? string.Empty))
                .ToArray();
            Assert.DoesNotContain(allTexts, t => t.Contains("重置", StringComparison.Ordinal));
            Assert.Contains(allTexts, t => t.Contains("DEV-LOCAL-01", StringComparison.Ordinal));
        });

    [Fact]
    public void All_views_render_capsule_action_buttons_with_uniform_pill_radii_and_un_stretched_heights() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("DEV-1", "主力工作站", true, "online", true, true, true, "192.168.1.10:42461");
            var discovered = new DeviceSnapshotPayload("DEV-2", "Pixel 9", false, "online", null, null, null, "192.168.1.11:42461");
            var prompt = new FilePromptSnapshotPayload("tr-1", "DEV-1", "very-long-file-name-for-layout-wrap-check.tar.zst", 2048, 30_000);
            var active = new TransferSnapshotPayload("tr-2", "DEV-1", "send", "archive.zip", 2, 5, false, 10_000);
            var history = new LocalTransferHistoryEntry("tr-0", "DEV-1", "主力工作站", "receive", "notes.txt", 512, "done", DateTimeOffset.UtcNow, "/tmp");

            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalDeviceId = "DEV-SELF",
                LocalDisplayName = "Workstation",
                LocalAddresses = ["192.168.1.50"],
                QuicPort = 42461,
                Devices = [peer, discovered],
                FilePrompts = [prompt],
                ActiveTransfers = [active],
                History = [history],
            });

            var tdView = new ThisDeviceView(new ThisDeviceViewModel(client, new FakeDaemonController(), new ShellPreferences(), _ => { }));
            var connView = new ConnectionView(new ConnectionViewModel(client));
            var trView = new TransfersView(new TransfersViewModel(client));
            var devVm = new DevicesViewModel(client);
            devVm.SelectDevice("DEV-2");
            var devView = new DevicesView(devVm);

            foreach (Control view in new Control[] { tdView, connView, trView, devView })
            {
                var win = new Window { Width = 1040, Height = 820, Content = view };
                win.Measure(new Size(1040, 820));
                win.Arrange(new Rect(0, 0, 1040, 820));

                foreach (var btn in view.GetLogicalDescendants().OfType<Button>())
                {
                    if (!btn.IsVisible || btn.Bounds.Height <= 0)
                    {
                        continue;
                    }

                    Assert.True(
                        btn.Bounds.Height <= 46.0,
                        $"Button '{btn.Name ?? btn.Content?.ToString()}' stretched to height {btn.Bounds.Height:F1}dp in {view.GetType().Name}.");

                    if (btn.Classes.Any(c => c is "Filled" or "FilledTonal" or "Outlined") && btn.Bounds.Height >= 38.0)
                    {
                        Assert.Equal(new CornerRadius(20), btn.CornerRadius);
                    }
                }
            }
        });

    private sealed class FakeDaemonController : IDaemonController
    {
        public bool IsRunningFromAppImage => false;
        public int ResetCalls { get; private set; }
        public DaemonStartResult NextStartResult { get; set; } = new(DaemonStartOutcome.Started);

        public Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default) =>
            Task.FromResult(NextStartResult);

        public Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true));

        public Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default)
        {
            ResetCalls++;
            return Task.FromResult(new DaemonActionResult(true));
        }
    }
}
