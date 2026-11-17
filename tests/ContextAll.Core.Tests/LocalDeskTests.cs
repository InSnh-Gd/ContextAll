using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class LocalDeskTests
{
    [Fact]
    public void Local_commands_return_type_96_and_a_quic_local_type_keeps_trust()
    {
        var desk = new LocalDesk();
        desk.FilePrompts.Add(new FilePromptSnapshotPayload("aa", "ca1peer", "a.txt", 4, 1000));
        desk.Transfers.Add(new TransferSnapshotPayload("bb", "ca1peer", "receive", "a.txt", 0, 1, false));
        var snapshot = ControlJson.Decode(desk.SnapshotFrame().Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value!;
        Assert.Single(snapshot.FilePrompts);
        Assert.Single(snapshot.Transfers);
        Assert.Equal(0, snapshot.Transfers[0].CompletedSegments);

        for (var type = 64; type <= 77; type++)
        {
            var command = FrameCodec.Decode(FrameCodec.Encode(
                ControlJson.Encode(new LocalCommandPayload(type.ToString("x2"), null, "Desk", null, null, null), ProtocolJsonContext.Default.LocalCommandPayload).Value!,
                (byte)type,
                0,
                0,
                0).Value!).Value!;
            var result = desk.Handle(command);
            Assert.Equal(FrameTypes.CommandResult, result.Type);
            Assert.Equal(96, result.Type);
        }

        var connectInvalid = FrameCodec.Decode(FrameCodec.Encode(
            ControlJson.Encode(new LocalCommandPayload("76bad0", null, null, null, null, null, "", 0), ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            FrameTypes.ConnectEndpoint,
            0,
            0,
            0).Value!).Value!;
        var connectInvalidResult = ControlJson.Decode(desk.Handle(connectInvalid).Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value!;
        Assert.False(connectInvalidResult.Ok);
        Assert.Equal("invalid-endpoint", connectInvalidResult.Error);

        var connectOk = FrameCodec.Decode(FrameCodec.Encode(
            ControlJson.Encode(new LocalCommandPayload("76aa01", null, null, null, null, null, "192.168.1.50", 4242, "Studio"), ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            FrameTypes.ConnectEndpoint,
            0,
            0,
            0).Value!).Value!;
        Assert.True(ControlJson.Decode(desk.Handle(connectOk).Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value!.Ok);

        var tooLarge = FrameCodec.Decode(FrameCodec.Encode(
            ControlJson.Encode(new LocalCommandPayload("77aa01", null, null, null, null, null, TotalBytes: FrameLimits.MaxClipboardBytes + 1), ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            FrameTypes.PushClipboardText,
            0,
            0,
            0).Value!).Value!;
        var tooLargeResult = ControlJson.Decode(desk.Handle(tooLarge).Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value!;
        Assert.False(tooLargeResult.Ok);
        Assert.Equal("too-large", tooLargeResult.Error);

        var chunkOk = FrameCodec.Decode(FrameCodec.Encode(
            ControlJson.Encode(
                new LocalCommandPayload("77aa02", null, null, null, null, null, TotalBytes: 5, Offset: 0, ChunkBase64: Convert.ToBase64String("hello"u8)),
                ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            FrameTypes.PushClipboardText,
            0,
            0,
            0).Value!).Value!;
        Assert.True(ControlJson.Decode(desk.Handle(chunkOk).Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value!.Ok);

        var duplicate = desk.Handle(chunkOk);
        var duplicateResult = ControlJson.Decode(duplicate.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value!;
        Assert.False(duplicateResult.Ok);
        Assert.Equal("duplicate-request", duplicateResult.Error);

        var trust = System.Collections.Immutable.ImmutableArray.Create(new TrustEntry("ca1peer", "peer", "aaaa", false, true));
        var state = new ChannelState(true, trust);
        var localOnQuic = FrameCodec.Encode(
            ControlJson.Encode(new LocalCommandPayload("abc", null, null, null, null, null), ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            FrameTypes.StartPairing,
            0,
            0,
            0).Value!;
        var closed = ChannelRules.OnQuicFrame(state, localOnQuic);
        Assert.False(closed.Open);
        Assert.Equal(trust, closed.Trust);
    }
}
