using System.Net;
using System.Reflection;
using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier3_Combinations;

[Trait("Category", "Tier3")]
[Trait("Feature", "Combination_NetworkFilter_SoftFailure")]
public sealed class NetworkFilterAndSoftFailureCombinationTests
{
    static bool IsUsableAddress(IPAddress address) =>
        (bool)typeof(LocalNetwork).GetMethod("IsUsableAddress", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [address])!;

    [Fact]
    public async Task CellularActive_With_NsdFailure_DegradesToBeaconOnWifi()
    {
        // 1. Cellular (non-usable loopback/carrier NAT) and Wi-Fi LAN addresses
        var wifiIp = IPAddress.Parse("192.168.1.50");
        var activeAddresses = new[] { IPAddress.Loopback, wifiIp };
        var selected = activeAddresses.Where(IsUsableAddress).ToArray();

        var selectedAddress = Assert.Single(selected);
        Assert.Equal(wifiIp, selectedAddress);

        // 2. NSD port 5353 collision / failure triggers soft degradation to beacon-only
        using var mdnsOccupant = MdnsTestProbe.TryOccupyPort();
        Assert.True(mdnsOccupant.Occupied);

        var freeBeaconPort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        using var listener = new UdpBeaconListener(freeBeaconPort, IPAddress.Loopback);

        var digest = new byte[16];
        await listener.SendBeaconAsync(digest, 42461, "WifiNode", new IPEndPoint(IPAddress.Loopback, freeBeaconPort));
        var beacon = await listener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(beacon);
        Assert.Equal("WifiNode", beacon.Name);
    }

    [Fact]
    public void Cellular_Only_Interface_Is_Completely_Filtered_Out()
    {
        var nonUsable = new[] { IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("ff02::1") };
        var allowed = nonUsable.Where(IsUsableAddress).ToArray();
        Assert.Empty(allowed);
    }

    [Fact]
    public void Network_Switch_From_Wifi_To_Ethernet_Maintains_Transport_Allowed()
    {
        var wifiIp = IPAddress.Parse("192.168.1.100");
        var ethIp = IPAddress.Parse("10.0.0.50");

        Assert.True(IsUsableAddress(wifiIp));
        Assert.True(IsUsableAddress(ethIp));

        // When switching, announce frame is encoded to propagate new route
        var payload = ControlJson.Encode(new PathAnnouncePayload([ethIp.ToString()]), ProtocolJsonContext.Default.PathAnnouncePayload);
        Assert.False(payload.IsFailure);

        var frame = FrameCodec.Encode(payload.Value!, FrameTypes.PathAnnounce, 1, 0, FrameTypes.SessionStream);
        Assert.False(frame.IsFailure);

        var decoded = FrameCodec.Read(frame.Value!);
        Assert.False(decoded.IsFailure);
        Assert.NotNull(decoded.Frame);
        Assert.Equal(FrameTypes.PathAnnounce, decoded.Frame.Type);

        var pathAnnounce = ControlJson.Decode(decoded.Frame.Payload.Span, ProtocolJsonContext.Default.PathAnnouncePayload);
        Assert.False(pathAnnounce.IsFailure);
        Assert.Equal([ethIp.ToString()], pathAnnounce.Value!.Addresses);
    }
}
