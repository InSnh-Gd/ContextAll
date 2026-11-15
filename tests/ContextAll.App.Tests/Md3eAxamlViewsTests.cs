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
