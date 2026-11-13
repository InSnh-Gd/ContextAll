using System.Buffers.Binary;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B05")]
public sealed class B05_SessionHostFaultToleranceTests
{
    [Fact]
    public void ChannelRules_Receiving_Local_Frame_Closes_Channel()
    {
        var state = new ChannelState(true, []);
        var header = new byte[FrameLimits.HeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0);
        header[4] = FrameTypes.StartPairing; // Type 64 >= 64
        header[5] = 0; // Priority 0

        var nextState = ChannelRules.OnQuicFrame(state, header);
        Assert.False(nextState.Open);
    }

    [Fact]
    public void ChannelRules_Receiving_CommandResult_Closes_Channel()
    {
        var state = new ChannelState(true, []);
        var header = new byte[FrameLimits.HeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0);
        header[4] = FrameTypes.CommandResult; // Type 96 >= 64
        header[5] = 0;

        var nextState = ChannelRules.OnQuicFrame(state, header);
        Assert.False(nextState.Open);
    }

    [Fact]
    public void FrameCodec_Rejects_Non_Zero_Reserved_Field()
    {
        var header = new byte[FrameLimits.HeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0); // length 0
        header[4] = FrameTypes.Hello; // Type 1
        header[5] = 0; // Priority 0
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 1); // reserved = 1 != 0

        var result = FrameCodec.Read(header);
        Assert.True(result.IsFailure);
        Assert.Equal("reserved", result.Reason);
    }

    [Fact]
    public void FrameCodec_Rejects_Payload_Exceeding_64KiB()
    {
        var header = new byte[FrameLimits.HeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)FrameLimits.MaxPayloadBytes + 1);
        header[4] = FrameTypes.Hello;

        var result = FrameCodec.Read(header);
        Assert.True(result.IsFailure);
        Assert.Equal("payload-length", result.Reason);
    }

    [Fact]
    public void FrameCodec_Rejects_Priority_Greater_Than_3()
    {
        var header = new byte[FrameLimits.HeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0);
        header[4] = FrameTypes.Hello;
        header[5] = 4; // Priority 4 > 3

        var result = FrameCodec.Read(header);
        Assert.True(result.IsFailure);
        Assert.Equal("priority", result.Reason);
    }
}
