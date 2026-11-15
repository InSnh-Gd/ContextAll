using ContextAll.App.ViewModels;
using ContextAll.App.Views;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class DevicesAndConnectionViewModelTests
{
    [Fact]
    public void DevicesView_adapts_between_wide_master_detail_and_narrow_drill_in() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var trusted = new DeviceSnapshotPayload("DEV111111111", "工作站", true, "online", true, false, true, "192.168.1.5:42461");
            var discovered = new DeviceSnapshotPayload("DEV222222222", "笔记本", false, "online", null, null, null, "192.168.1.6:42461");
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [trusted, discovered],
            });

            var vm = new DevicesViewModel(client);
            var view = new DevicesView(vm);

            vm.ApplyWidth(900);
            Assert.True(view.IsListPaneVisible);
            Assert.True(view.IsDetailPaneVisible);
            Assert.False(vm.CanNavigateBack);

            vm.ApplyWidth(600);
            Assert.True(view.IsListPaneVisible);
            Assert.False(view.IsDetailPaneVisible);
            Assert.False(vm.CanNavigateBack);

            vm.SelectDevice("DEV111111111");
            Assert.False(view.IsListPaneVisible);
            Assert.True(view.IsDetailPaneVisible);
            Assert.True(vm.CanNavigateBack);

            vm.ClearSelection();
            Assert.True(view.IsListPaneVisible);
            Assert.False(view.IsDetailPaneVisible);
        });

    [Fact]
    public Task DevicesViewModel_handles_pairing_switches_unpair_confirmation_and_clipboard_push() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var trusted = new DeviceSnapshotPayload("DEV-ALPHA", "Alpha", true, "online", false, true);
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [trusted],
                PairingPrompt = new PairingSnapshotPayload("DEV-NEW", "新手机", "482913", 30000),
            });

            var vm = new DevicesViewModel(client);
            var view = new DevicesView(vm);
            Assert.True(view.IsPairingBannerVisible);
            Assert.Equal("482913", view.DisplayedPairingCode);

            Assert.True(await vm.AcceptPairingAsync());
            Assert.True(await vm.RejectPairingAsync());
            Assert.True(await vm.SetClipboardSyncAsync("DEV-ALPHA", true));
            Assert.True(await vm.SetAskBeforeSaveAsync("DEV-ALPHA", false));

            vm.RequestUnpair("DEV-ALPHA");
            Assert.Equal("DEV-ALPHA", vm.ConfirmingUnpairDeviceId);
            Assert.True(await vm.ConfirmUnpairAsync());

            vm.OpenClipboardModal();
            Assert.True(view.IsClipboardModalVisible);
            vm.ClipboardDraftText = new string('A', FrameLimits.MaxClipboardBytes + 1);
            Assert.True(vm.IsClipboardTooLarge);
            Assert.False(await vm.PushClipboardAsync());

            vm.ClipboardDraftText = "你好 ContextAll";
            Assert.False(vm.IsClipboardTooLarge);
            Assert.True(await vm.PushClipboardAsync());
            Assert.False(view.IsClipboardModalVisible);

            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.AcceptPairing && c.Payload.DeviceId == "DEV-NEW");
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.RejectPairing && c.Payload.DeviceId == "DEV-NEW");
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.SetClipboardSync && c.Payload.DeviceId == "DEV-ALPHA" && c.Payload.Enabled == true);
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.SetAskBeforeSave && c.Payload.DeviceId == "DEV-ALPHA" && c.Payload.Enabled == false);
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.UnpairLocal && c.Payload.DeviceId == "DEV-ALPHA");
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.PushClipboardText && c.Payload.Text == "你好 ContextAll");
        });

    [Fact]
    public Task ConnectionViewModel_parses_endpoints_refuses_invalid_inputs_and_records_guide_seen() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalAddresses = ["192.168.10.22", "fd00::5"],
                QuicPort = 42461,
                ClipboardBackend = "wayland",
            });

            bool? guideSeenRecorded = null;
            var vm = new ConnectionViewModel(client, seen => guideSeenRecorded = seen);
            var view = new ConnectionView(vm);

            Assert.Equal(["192.168.10.22:42461", "[fd00::5]:42461"], vm.LocalEndpoints);
            Assert.Contains("Wayland 原生", view.ClipboardStatusText);

            vm.EndpointInput = "not-an-endpoint";
            Assert.False(await vm.DialEndpointAsync());
            Assert.True(vm.DialIsError);
            Assert.Empty(client.SentCommands);

            vm.EndpointInput = "192.168.1.99:42461";
            vm.AliasInput = "办公室主力机";
            Assert.True(await vm.DialEndpointAsync());
            Assert.Contains(client.SentCommands, c =>
                c.FrameType == FrameTypes.ConnectEndpoint &&
                c.Payload.Host == "192.168.1.99" &&
                c.Payload.Port == 42461 &&
                c.Payload.Alias == "办公室主力机");

            vm.EndpointInput = "[::1]:50000";
            vm.AliasInput = "";
            Assert.True(await vm.DialEndpointAsync());
            Assert.Contains(client.SentCommands, c =>
                c.FrameType == FrameTypes.ConnectEndpoint &&
                c.Payload.Host == "::1" &&
                c.Payload.Port == 50000 &&
                c.Payload.Alias == null);

            vm.OpenGuide();
            Assert.True(view.IsGuideVisible);
            vm.CloseGuide();
            Assert.False(view.IsGuideVisible);
            Assert.True(guideSeenRecorded);
        });
}
