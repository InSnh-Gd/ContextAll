using ContextAll.Protocol;

namespace ContextAll.Protocol.Tests;

public sealed class FrameCodecTests
{
    [Fact]
    public void Header_round_trips_and_illegal_headers_are_failure_values()
    {
        var payload = new byte[] { 9, 8, 7, 6 };
        var encoded = FrameCodec.Encode(payload, type: 15, priority: 1, flags: 0x0102, streamId: 7);
        var thrown = Record.Exception(() =>
        {
            _ = FrameCodec.Encode(new byte[FrameLimits.MaxPayloadBytes + 1], 1, 0, 0, 0);
            _ = FrameCodec.Encode(payload, 1, 0, 0, 0, reserved: 1);
            _ = FrameCodec.Decode(ReservedHeader());
            _ = FrameCodec.Decode(TooLongHeader());
        });
        var decoded = FrameCodec.Decode(encoded.Value!);

        Assert.Null(thrown);
        Assert.False(encoded.IsFailure);
        Assert.False(decoded.IsFailure);
        Assert.Equal(payload.Length, decoded.Value!.PayloadLength);
        Assert.Equal(15, decoded.Value.Type);
        Assert.Equal(1, decoded.Value.Priority);
        Assert.Equal((ushort)0x0102, decoded.Value.Flags);
        Assert.Equal(7u, decoded.Value.StreamId);
        Assert.Equal(payload, decoded.Value.Payload.ToArray());
        Assert.True(FrameCodec.Encode(payload, 1, 0, 0, 0, reserved: 1).IsFailure);
        Assert.True(FrameCodec.Encode(new byte[FrameLimits.MaxPayloadBytes + 1], 1, 0, 0, 0).IsFailure);
        Assert.Equal("reserved", FrameCodec.Decode(ReservedHeader()).Reason);
        Assert.Equal("payload-length", FrameCodec.Decode(TooLongHeader()).Reason);

        var hello = ControlJson.Encode(
            new HelloPayload("pairing", "desk"),
            ProtocolJsonContext.Default.HelloPayload);
        var helloBack = ControlJson.Decode(hello.Value!, ProtocolJsonContext.Default.HelloPayload);
        Assert.False(hello.IsFailure);
        Assert.Equal("pairing", helloBack.Value!.Intent);
        Assert.Equal("desk", helloBack.Value.DisplayName);
    }

    static byte[] ReservedHeader()
    {
        var bytes = new byte[FrameLimits.HeaderBytes];
        bytes[15] = 1;
        return bytes;
    }

    static byte[] TooLongHeader()
    {
        var bytes = new byte[FrameLimits.HeaderBytes];
        bytes[1] = 1;
        bytes[3] = 1;
        return bytes;
    }
}
