using ContextAll.Protocol;

namespace ContextAll.Protocol.Tests;

public sealed class FrameLimitsTests
{
    [Fact]
    public void Header_is_16_bytes_and_payload_is_capped_at_64_kib()
    {
        Assert.Equal(16, FrameLimits.HeaderBytes);
        Assert.Equal(64 * 1024, FrameLimits.MaxPayloadBytes);
    }
}
