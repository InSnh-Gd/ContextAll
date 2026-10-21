using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class RouteRulesTests
{
    static readonly byte[] Local = Enumerable.Range(0, 91).Select(i => (byte)i).ToArray();
    static readonly byte[] Peer = Enumerable.Range(0, 80).Select(i => (byte)((i * 3) % 256)).ToArray();

    [Fact]
    public void A_beacon_round_trips_and_the_local_digest_is_ignored()
    {
        var digest = DeviceIds.Digest(Peer);
        var encoded = BeaconCodec.Encode(digest, 42461, "phone");
        var decoded = BeaconCodec.Decode(encoded.Value!);

        Assert.False(encoded.IsFailure);
        Assert.Equal((byte)'C', encoded.Value![0]);
        Assert.Equal((byte)'A', encoded.Value[1]);
        Assert.Equal((byte)'0', encoded.Value[2]);
        Assert.Equal((byte)'1', encoded.Value[3]);
        Assert.Equal(1, encoded.Value[4]);
        Assert.Equal(digest, decoded.Value!.Digest);
        Assert.Equal((ushort)42461, decoded.Value.Port);
        Assert.Equal("phone", decoded.Value.Name);

        var table = new RouteTable(Local);
        var id = DeviceIds.Text(Peer);
        table.AddDiscovery(id, digest, "10.0.0.5", 42461, 0, "bad\nname");
        table.AddDiscovery(DeviceIds.Text(Local), DeviceIds.Digest(Local), "10.0.0.4", 42461, 0, "self");

        Assert.Single(table.DiscoveryPaths(id));
        Assert.Null(table.DisplayNameOf(id));
        Assert.Empty(table.DiscoveryPaths(DeviceIds.Text(Local)));
    }

    [Fact]
    public void Discovery_expiry_and_a_closed_session_do_not_remove_trust_or_redial_the_session_address()
    {
        var table = new RouteTable(Local);
        var id = DeviceIds.Text(Peer);
        table.RememberTrust(id);
        table.AddDiscovery(id, DeviceIds.Digest(Peer), "10.0.0.5", 42461, 0, "phone");
        table.NoteSession(id, "10.0.0.9");
        table.CloseSession(id);
        table.Expire(180_000);

        Assert.True(table.IsTrusted(id));
        Assert.Empty(table.DiscoveryPaths(id));
        Assert.DoesNotContain(table.DialCandidates(id), path => path.Address == "10.0.0.9");

        table.AddDiscovery(id, DeviceIds.Digest(Peer), "10.0.0.5", 42461, 180_000, "phone");
        table.Announce(id, ["8.8.8.8", "169.254.1.5", "10.1.1.9"], [new OnLinkPrefix("10.0.0.0", 8)], 180_000);
        Assert.Contains(table.DialCandidates(id), path => path.Address == "10.1.1.9");
        Assert.DoesNotContain(table.DialCandidates(id), path => path.Address == "8.8.8.8");
        Assert.DoesNotContain(table.DialCandidates(id), path => path.Address == "169.254.1.5");
    }

    [Fact]
    public void An_untrusted_peer_is_not_dialed_until_pairing_and_a_failed_dial_waits_for_refresh()
    {
        var table = new RouteTable(Local);
        var id = DeviceIds.Text(Peer);
        table.AddDiscovery(id, DeviceIds.Digest(Peer), "10.0.0.5", 42461, 0, "phone");

        Assert.False(table.ShouldDial(id, startPairing: false));
        Assert.True(table.ShouldDial(id, startPairing: true));

        table.RememberTrust(id);
        table.FailDial(id, "10.0.0.5");
        Assert.False(table.ShouldDial(id, startPairing: false));
        table.AddDiscovery(id, DeviceIds.Digest(Peer), "10.0.0.5", 42461, 5_000, "phone");
        Assert.True(table.ShouldDial(id, startPairing: false));
    }
}
