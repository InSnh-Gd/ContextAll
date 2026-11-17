using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

public sealed class LocalShellReducerTests
{
    [Fact]
    public void Snapshot_and_events_update_devices_pairing_prompts_transfers_and_history()
    {
        var t0 = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        var state = LocalShellState.Empty;
        Assert.False(state.Connected);
        Assert.Equal(LocalConnectionStatus.Disconnected, state.ConnectionStatus);

        state = LocalShellReducer.OnConnecting(state);
        Assert.Equal(LocalConnectionStatus.Connecting, state.ConnectionStatus);
        Assert.False(state.Connected);

        var snapshot = new SnapshotPayload(
            "My Linux Desktop",
            "",
            [
                new DeviceSnapshotPayload("ca1peer1", "Phone", true, "connected", true, true, true, "192.168.1.20:42461"),
            ],
            null,
            [],
            [],
            LocalDeviceId: "ca1self",
            ResolvedDownloadDirectory: "/home/user/Downloads",
            LocalAddresses: ["192.168.1.10"],
            QuicPort: 42461,
            ClipboardBackend: "wayland");

        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.Snapshot, snapshot, ProtocolJsonContext.Default.SnapshotPayload), () => t0);
        Assert.True(state.Connected);
        Assert.Equal("ca1self", state.LocalDeviceId);
        Assert.Equal("My Linux Desktop", state.LocalDisplayName);
        Assert.Equal("/home/user/Downloads", state.ResolvedDownloadDirectory);
        Assert.Equal("wayland", state.ClipboardBackend);
        Assert.Single(state.Devices);
        Assert.Equal("192.168.1.20:42461", state.Devices[0].Endpoint);

        var discovered = new DeviceEventPayload("ca1peer2", "Laptop", false, "reachable", null, null, null, "192.168.1.33:42461");
        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.DeviceChanged, discovered, ProtocolJsonContext.Default.DeviceEventPayload), () => t0);
        Assert.Equal(2, state.Devices.Count);
        Assert.Equal("Laptop", state.Devices[1].DisplayName);

        var prompt = new PairingSnapshotPayload("ca1peer2", "Laptop", "482910", 55_000);
        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.PairingPrompt, prompt, ProtocolJsonContext.Default.PairingSnapshotPayload));
        Assert.Equal("482910", state.PairingPrompt?.Code);

        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.PairingEnded, new PairingEndedPayload("ca1peer2", "trusted"), ProtocolJsonContext.Default.PairingEndedPayload));
        Assert.Null(state.PairingPrompt);

        // DeviceChanged with Trusted=true (emitted on EnterTrusted) must also clear matching PairingPrompt.
        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.PairingPrompt, prompt, ProtocolJsonContext.Default.PairingSnapshotPayload));
        Assert.NotNull(state.PairingPrompt);
        state = LocalShellReducer.Apply(
            state,
            JsonFrame(
                FrameTypes.DeviceChanged,
                new DeviceEventPayload("ca1peer2", "Laptop", true, "connected", false, true, false, "192.168.1.33:42461"),
                ProtocolJsonContext.Default.DeviceEventPayload));
        Assert.Null(state.PairingPrompt);

        var filePrompt = new FilePromptSnapshotPayload("tx-1", "ca1peer1", "notes.pdf", 4096, 59_000);
        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.FilePrompt, filePrompt, ProtocolJsonContext.Default.FilePromptSnapshotPayload));
        Assert.Single(state.FilePrompts);

        var progress = new TransferSnapshotPayload("tx-1", "ca1peer1", "in", "notes.pdf", 1, 2, false, 4096);
        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.TransferProgress, progress, ProtocolJsonContext.Default.TransferSnapshotPayload));
        Assert.Empty(state.FilePrompts);
        Assert.Single(state.ActiveTransfers);
        Assert.Equal(4096, state.ActiveTransfers[0].TotalSize);

        var ended = new TransferEndedPayload("tx-1", "completed");
        state = LocalShellReducer.Apply(state, JsonFrame(FrameTypes.TransferEnded, ended, ProtocolJsonContext.Default.TransferEndedPayload), () => t0.AddSeconds(5));
        Assert.Empty(state.ActiveTransfers);
        var entry = Assert.Single(state.History);
        Assert.Equal("Phone", entry.DeviceName);
        Assert.Equal("notes.pdf", entry.FileName);
        Assert.Equal("completed", entry.Outcome);
        Assert.Equal("/home/user/Downloads", entry.SavedDirectory);
        Assert.Equal(4096, entry.TotalSize);

        state = LocalShellReducer.OnDisconnected(state, "eof");
        Assert.False(state.Connected);
        Assert.Empty(state.Devices);
        Assert.Empty(state.ActiveTransfers);
        Assert.Single(state.History);
        Assert.Equal("eof", state.LastDisconnectReason);
    }

    static Frame JsonFrame<T>(byte type, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        where T : class
    {
        var json = ControlJson.Encode(payload, info).Value!;
        var raw = FrameCodec.Encode(json, type, 0, 0, FrameTypes.SessionStream).Value!;
        return FrameCodec.Decode(raw).Value!;
    }
}
