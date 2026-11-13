using System.Net;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier4_RealWorldScenarios;

[Trait("Category", "Tier4")]
[Trait("Feature", "Scenario4_NetworkSwitch")]
public sealed class S04_NetworkSwitchWifiToEthernetScenarioTests
{
    [Fact]
    public async Task Scenario4_Interface_Migration_Generates_PathAnnounce_Frame()
    {
        // 1. Simulate mDNS collision to trigger soft-failure degradation to beacon-only
        using var mdnsCollision = MdnsTestProbe.TryOccupyPort();
        Assert.True(mdnsCollision.Occupied);

        var freeBeaconPort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        using var beaconListener = new UdpBeaconListener(freeBeaconPort, IPAddress.Loopback);

        // 2. Wi-Fi phase: Node broadcasts on Wi-Fi endpoint
        var wifiEndpoint = new IPEndPoint(IPAddress.Loopback, freeBeaconPort);
        var digest = new byte[16];
        Random.Shared.NextBytes(digest);

        await beaconListener.SendBeaconAsync(digest, 42461, "AndroidNode-Wifi", wifiEndpoint);
        var wifiBeacon = await beaconListener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(wifiBeacon);
        Assert.Equal("AndroidNode-Wifi", wifiBeacon.Name);

        // 3. Network Handover: Wi-Fi interface drops, Ethernet comes up
        var ethernetIp = IPAddress.Parse("10.0.0.25");
        var newEndpoints = QuicLink.ListenerEndpoints([ethernetIp]);
        var targetEp = Assert.Single(newEndpoints);
        Assert.Equal(ethernetIp, targetEp.Address);
        Assert.Equal(QuicLink.Port, targetEp.Port);

        // 4. Node issues PathAnnounce frame (Type 7) to active peers over session stream
        var announcePayload = ControlJson.Encode(
            new PathAnnouncePayload(["10.0.0.25"]),
            ProtocolJsonContext.Default.PathAnnouncePayload);
        Assert.False(announcePayload.IsFailure);

        var pathAnnounceWire = FrameCodec.Encode(announcePayload.Value!, FrameTypes.PathAnnounce, 1, 0, FrameTypes.SessionStream);
        Assert.False(pathAnnounceWire.IsFailure);

        var decodedFrame = FrameCodec.Read(pathAnnounceWire.Value!).Frame;
        Assert.NotNull(decodedFrame);
        Assert.Equal(FrameTypes.PathAnnounce, decodedFrame.Type);

        var decodedPayload = ControlJson.Decode(decodedFrame.Payload.Span, ProtocolJsonContext.Default.PathAnnouncePayload);
        Assert.False(decodedPayload.IsFailure);
        Assert.Equal(["10.0.0.25"], decodedPayload.Value!.Addresses);

        // 5. Node emits updated beacon announcing new Ethernet endpoint
        await beaconListener.SendBeaconAsync(digest, (ushort)targetEp.Port, "AndroidNode-Ethernet", wifiEndpoint);
        var ethBeacon = await beaconListener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(ethBeacon);
        Assert.Equal("AndroidNode-Ethernet", ethBeacon.Name);
    }
}
