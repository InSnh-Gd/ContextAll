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

        for (var type = 64; type <= 75; type++)
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
