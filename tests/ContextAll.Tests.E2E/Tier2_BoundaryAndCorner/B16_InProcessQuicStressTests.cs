using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B16")]
public sealed class B16_InProcessQuicStressTests
{
    [Fact]
    public void Quic_KeepAliveInterval_Is_15_Seconds()
    {
        using var cert = OpaqueQuicClient.GenerateValidP256Certificate();
        var options = QuicLink.ServerOptions(cert);
        Assert.Equal(TimeSpan.FromSeconds(15), options.KeepAliveInterval);
    }

    [Fact]
    public void Quic_IdleTimeout_Is_60_Seconds()
    {
        using var cert = OpaqueQuicClient.GenerateValidP256Certificate();
        var options = QuicLink.ServerOptions(cert);
        Assert.Equal(TimeSpan.FromSeconds(60), options.IdleTimeout);
    }

    [Fact]
    public void FrameCodec_Encodes_Max_Allowed_Payload_65536_Bytes()
    {
        var maxPayload = new byte[FrameLimits.MaxPayloadBytes];
        var outcome = FrameCodec.Encode(maxPayload, FrameTypes.FileSegment, 3, 0, 2);
        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.Equal(FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes, outcome.Value.Length);
    }

    [Fact]
    public void FrameCodec_Encode_Rejects_65537_Bytes()
    {
        var overPayload = new byte[FrameLimits.MaxPayloadBytes + 1];
        var outcome = FrameCodec.Encode(overPayload, FrameTypes.FileSegment, 3, 0, 2);
        Assert.True(outcome.IsFailure);
        Assert.Equal("payload-length", outcome.Reason);
    }

    [Fact]
    public void FrameCodec_Encode_Rejects_Invalid_Priority()
    {
        var outcome = FrameCodec.Encode(new byte[10], FrameTypes.FileSegment, priority: 4, flags: 0, streamId: 2);
        Assert.True(outcome.IsFailure);
        Assert.Equal("priority", outcome.Reason);
    }
}
