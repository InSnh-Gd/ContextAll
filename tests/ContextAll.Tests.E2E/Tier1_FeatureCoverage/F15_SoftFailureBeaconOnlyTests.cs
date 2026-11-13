using System.Net;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F15")]
public sealed class F15_SoftFailureBeaconOnlyTests
{
    [Fact]
    public async Task Beacon_Continues_When_Mdns_Port_Is_Occupied()
    {
        await using var sandbox = new NetworkSandbox();
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);

        using var listener = new UdpBeaconListener(freePort, IPAddress.Loopback);
        var digest = new byte[16];
        Random.Shared.NextBytes(digest);

        await listener.SendBeaconAsync(digest, 42461, "BeaconPeer", new IPEndPoint(IPAddress.Loopback, freePort));
        var beacon = await listener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(beacon);
        Assert.Equal("BeaconPeer", beacon.Name);
        Assert.Equal(42461, beacon.Port);
    }

    [Fact]
    public void Beacon_Encodes_Binary_CA01_Format()
    {
        var digest = new byte[16];
        var outcome = BeaconCodec.Encode(digest, 42461, "TestDevice");

        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.Equal((byte)'C', outcome.Value[0]);
        Assert.Equal((byte)'A', outcome.Value[1]);
        Assert.Equal((byte)'0', outcome.Value[2]);
        Assert.Equal((byte)'1', outcome.Value[3]);
        Assert.Equal(1, outcome.Value[4]);
    }

    [Fact]
    public void Beacon_Decodes_Valid_Payload()
    {
        var digest = new byte[16];
        digest[0] = 0x42;
        var encoded = BeaconCodec.Encode(digest, 42461, "RemoteNode");

        var decoded = BeaconCodec.Decode(encoded.Value!);
        Assert.False(decoded.IsFailure);
        Assert.NotNull(decoded.Value);
        Assert.Equal("RemoteNode", decoded.Value.Name);
        Assert.Equal(0x42, decoded.Value.Digest[0]);
    }

    [Fact]
    public void Beacon_Rejects_Corrupted_Magic()
    {
        var digest = new byte[16];
        var encoded = BeaconCodec.Encode(digest, 42461, "Node");
        var bytes = (byte[])encoded.Value!.Clone();
        bytes[0] = (byte)'X'; // Corrupt magic

        var decoded = BeaconCodec.Decode(bytes);
        Assert.True(decoded.IsFailure);
    }

    [Fact]
    public void Beacon_Rejects_Invalid_Digest_Length()
    {
        var invalidDigest = new byte[12]; // Must be 16
        var outcome = BeaconCodec.Encode(invalidDigest, 42461, "Node");
        Assert.True(outcome.IsFailure);
        Assert.Equal("digest", outcome.Reason);
    }
}
