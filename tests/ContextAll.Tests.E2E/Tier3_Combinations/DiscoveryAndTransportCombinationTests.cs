using System.Net;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier3_Combinations;

[Trait("Category", "Tier3")]
[Trait("Feature", "Combination_Discovery_Transport")]
public sealed class DiscoveryAndTransportCombinationTests
{
    [Fact]
    public async Task Beacon_Discovery_Yields_Endpoint_For_Quic_Connection()
    {
        var freeBeaconPort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var freeQuicPort = 42461;

        using var beaconListener = new UdpBeaconListener(freeBeaconPort, IPAddress.Loopback);
        var digest = new byte[16];
        Random.Shared.NextBytes(digest);

        // Remote peer emits beacon announcing QUIC port
        await beaconListener.SendBeaconAsync(digest, (ushort)freeQuicPort, "DiscoveredPeer", new IPEndPoint(IPAddress.Loopback, freeBeaconPort));
        var beacon = await beaconListener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(beacon);
        Assert.Equal((ushort)freeQuicPort, beacon.Port);
        Assert.Equal("DiscoveredPeer", beacon.Name);

        if (!QuicLink.Supported)
        {
            using var serverCertUnsup = OpaqueQuicClient.GenerateValidP256Certificate();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QuicLink.Listen(IPAddress.Loopback, serverCertUnsup, freeQuicPort));
            Assert.Equal("quic-unsupported", ex.Message);
            return;
        }

        // Remote peer starts QUIC listener on freeQuicPort
        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate();
        var quicEndpoint = new IPEndPoint(IPAddress.Loopback, freeQuicPort);
        await using var listener = await QuicLink.Listen(quicEndpoint.Address, serverCert, quicEndpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        // Discovering node immediately connects over QUIC to the discovered port
        var targetEndpoint = new IPEndPoint(IPAddress.Loopback, beacon.Port);
        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(targetEndpoint);
        await using var serverConn = await acceptTask;

        Assert.NotNull(serverConn);
        Assert.Equal("contextall", client.Connection!.NegotiatedApplicationProtocol.ToString());
    }

    [Fact]
    public void Mdns_Discovery_Yields_Endpoint_With_Valid_DeviceId()
    {
        const string deviceId = "ca1aaaaaaaaaaaaaaaaaaaaaaaaaa";
        var address = IPAddress.Parse("127.0.0.1");
        var packet = MdnsCodec.EncodeResponse(deviceId, "MobileNode", 42461, address);

        var read = MdnsCodec.Read(packet, IPAddress.Loopback);
        var service = Assert.Single(read.Services);

        Assert.Equal(deviceId, service.DeviceId);
        Assert.Equal(42461, service.Port);
        Assert.Equal(address, service.Address);
    }

    [Fact]
    public async Task Discovery_To_Quic_Handshake_Complete_Cycle()
    {
        var freeBeaconPort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var freeQuicPort = 42461;

        // 1. Discovery phase
        using var beaconListener = new UdpBeaconListener(freeBeaconPort, IPAddress.Loopback);
        var digest = new byte[16];
        Random.Shared.NextBytes(digest);
        await beaconListener.SendBeaconAsync(digest, (ushort)freeQuicPort, "ServerNode", new IPEndPoint(IPAddress.Loopback, freeBeaconPort));
        var discovered = await beaconListener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(discovered);
        Assert.Equal((ushort)freeQuicPort, discovered.Port);

        if (!QuicLink.Supported)
        {
            using var serverCertUnsup = OpaqueQuicClient.GenerateValidP256Certificate();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QuicLink.Listen(IPAddress.Loopback, serverCertUnsup, freeQuicPort));
            Assert.Equal("quic-unsupported", ex.Message);
            return;
        }

        var endpoint = new IPEndPoint(IPAddress.Loopback, freeQuicPort);
        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate();
        await using var listener = await QuicLink.Listen(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        // 2. Transport handshake phase
        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, discovered.Port));
        await using var serverConn = await acceptTask;

        Assert.NotNull(serverConn);
        Assert.Equal("contextall", client.Connection!.NegotiatedApplicationProtocol.ToString());

        // 3. Stream exchange phase
        await client.OpenSessionStreamAsync();
        await using var serverStream = await serverConn.AcceptInboundStreamAsync();

        var payload = "{\"intent\":\"pairing\",\"displayName\":\"ClientNode\"}"u8.ToArray();
        await client.SendRawFrameAsync(FrameTypes.Hello, 0, 0, FrameTypes.SessionStream, payload);

        var header = new byte[FrameLimits.HeaderBytes];
        var readBytes = await serverStream.ReadAsync(header);
        Assert.Equal(FrameLimits.HeaderBytes, readBytes);
        Assert.Equal(FrameTypes.Hello, header[4]);
    }

    [Fact]
    public void Beacon_Addresses_Map_To_Quic_Listener_Endpoints()
    {
        var addresses = new[] { IPAddress.Parse("192.168.1.10"), IPAddress.Parse("192.168.1.20") };
        var endpoints = QuicLink.ListenerEndpoints(addresses);

        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, ep => Assert.Equal(QuicLink.Port, ep.Port));
    }
}
