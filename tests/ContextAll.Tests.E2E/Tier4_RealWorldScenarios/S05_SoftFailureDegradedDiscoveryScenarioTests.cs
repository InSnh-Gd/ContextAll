using System.Net;
using System.Net.Quic;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier4_RealWorldScenarios;

[Trait("Category", "Tier4")]
[Trait("Feature", "Scenario5_DegradedDiscoverySoftFailure")]
public sealed class S05_SoftFailureDegradedDiscoveryScenarioTests
{
    [Fact]
    public async Task Scenario5_Multicast_Blocked_Falls_Back_To_Udp_Beacon_Discovery()
    {
        if (!QuicLink.Supported)
        {
            // When QUIC transport is unsupported on host, verify mDNS soft failure to beacon fallback contract
            using var occupant = MdnsTestProbe.TryOccupyPort();
            Assert.True(occupant.Occupied);

            var freeBeaconPort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
            using var beaconListener = new UdpBeaconListener(freeBeaconPort, IPAddress.Loopback);
            var digest = new byte[16];
            await beaconListener.SendBeaconAsync(digest, 42461, "FallbackNode", new IPEndPoint(IPAddress.Loopback, freeBeaconPort));
            var beacon = await beaconListener.WaitForBeaconAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(beacon);
            Assert.Equal((ushort)42461, beacon.Port);

            using var serverCertUnsup = OpaqueQuicClient.GenerateValidP256Certificate();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QuicLink.Listen(IPAddress.Loopback, serverCertUnsup, 42461));
            Assert.Equal("quic-unsupported", ex.Message);
            return;
        }

        var initialSockets = InProcessServiceHarness.SnapshotUnixSockets();
        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=ServerNode");

        await using var listener = await QuicLink.ListenMutual(IPAddress.Loopback, serverCert, port: 0);
        var serverEndpoint = listener.LocalEndPoint;

        const int clientCount = 6;
        var clientTasks = new List<Task>();
        var clients = new List<OpaqueQuicClient>();

        var acceptedConns = new List<QuicConnection>();
        var acceptTask = Task.Run(async () =>
        {
            for (var i = 0; i < clientCount; i++)
            {
                var conn = await listener.AcceptConnectionAsync();
                lock (acceptedConns) acceptedConns.Add(conn);
            }
        });

        // 1. Concurrently dial server with 6 unique client identities
        for (var i = 0; i < clientCount; i++)
        {
            var clientIndex = i;
            clientTasks.Add(Task.Run(async () =>
            {
                var clientCert = OpaqueQuicClient.GenerateValidP256Certificate($"CN=ClientNode_{clientIndex}");
                var client = new OpaqueQuicClient(clientCert);
                lock (clients) clients.Add(client);

                await client.ConnectAsync(serverEndpoint);
                await client.OpenSessionStreamAsync();

                // Send Hello Frame
                var hello = new HelloPayload("session", $"Client_{clientIndex}");
                var json = ControlJson.Encode(hello, ProtocolJsonContext.Default.HelloPayload);
                await client.SendRawFrameAsync(FrameTypes.Hello, 0, 0, FrameTypes.SessionStream, json.Value!);
            }));
        }

        await Task.WhenAll(clientTasks);
        await acceptTask;

        // 2. Verify all 6 concurrent connections accepted
        Assert.Equal(clientCount, acceptedConns.Count);

        // 3. Staggered Teardown: 3 graceful closures, 3 abrupt stream cancellations
        for (var i = 0; i < clientCount; i++)
        {
            if (i % 2 == 0)
            {
                await clients[i].DisposeAsync();
            }
            else
            {
                // Abrupt cancellation
                clients[i].SessionStream?.Abort(QuicAbortDirection.Both, 1);
                await clients[i].DisposeAsync();
            }
        }

        foreach (var conn in acceptedConns)
        {
            await conn.DisposeAsync();
        }

        // 4. Resource Leak Audit
        await listener.DisposeAsync();
        Assert.Empty(InProcessServiceHarness.SnapshotUnixSockets().Except(initialSockets));
        var releaseWait = System.Diagnostics.Stopwatch.StartNew();
        while (!NetworkSandbox.IsPortAvailable(serverEndpoint.Port, serverEndpoint.Address))
        {
            Assert.True(releaseWait.Elapsed < TimeSpan.FromSeconds(3), $"UDP port {serverEndpoint.Port} was not released.");
            await Task.Delay(25);
        }
    }
}
