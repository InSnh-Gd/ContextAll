using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B15")]
public sealed class B15_SoftFailureRecoveryTests
{
    [Fact]
    public void Beacon_Interval_Is_5_Seconds()
    {
        var field = typeof(LanHost).GetField("BeaconIntervalMs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(5000, (int)field.GetValue(null)!);
    }

    [Fact]
    public void RouteTable_Expires_Candidates_After_3_Minutes()
    {
        var localSpki = new byte[16];
        var table = new RouteTable(localSpki);

        var peerDigest = new byte[16];
        peerDigest[0] = 0xAA;

        const long nowMs = 1000;
        table.AddDiscovery("ca1peer123", peerDigest, "192.168.1.50", 42461, nowMs, "PeerA");
        Assert.Single(table.DiscoveryPaths("ca1peer123"));

        // Before expiration (< 180,000 ms), route is present
        table.Expire(nowMs + 100_000);
        Assert.Single(table.DiscoveryPaths("ca1peer123"));

        // At or after 180,000 ms, candidate is removed
        table.Expire(nowMs + 180_000);
        Assert.Empty(table.DiscoveryPaths("ca1peer123"));
    }

    [Fact]
    public void Beacon_Codec_Rejects_Packet_With_Mismatched_Declared_Length()
    {
        var digest = new byte[16];
        var encoded = BeaconCodec.Encode(digest, 42461, "Device");
        var bytes = (byte[])encoded.Value!.Clone();

        // Mismatched declared length at bytes 23..24
        bytes[23] = 0xFF;
        bytes[24] = 0xFF;

        var decoded = BeaconCodec.Decode(bytes);
        Assert.True(decoded.IsFailure);
        Assert.Equal("length", decoded.Reason);
    }

    [Fact]
    public void Beacon_Codec_Rejects_Packet_Smaller_Than_Header()
    {
        var smallBytes = new byte[24]; // Minimum beacon size is 25
        var decoded = BeaconCodec.Decode(smallBytes);
        Assert.True(decoded.IsFailure);
        Assert.Equal("beacon", decoded.Reason);
    }

    [Fact]
    public void Beacon_Codec_Encodes_DisplayNames_Up_To_64_Characters()
    {
        var digest = new byte[16];
        var longName = new string('A', 64);
        var outcome = BeaconCodec.Encode(digest, 42461, longName);

        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
    }
}
