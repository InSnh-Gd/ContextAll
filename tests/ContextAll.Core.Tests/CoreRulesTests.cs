using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class CoreRulesTests
{
    [Fact]
    public void Payload_cap_matches_the_protocol()
    {
        Assert.Equal(FrameLimits.MaxPayloadBytes, CoreRules.MaxPayloadBytes);
    }
}
