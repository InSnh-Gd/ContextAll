using System.Net;
using System.Net.Quic;
using ContextAll.Effects;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F02")]
public sealed class F02_LoopbackQuicSpikeTests
{
    [Fact]
    public void QuicListener_IsSupported_Returns_Boolean()
    {
        Assert.Equal(QuicListener.IsSupported, QuicLink.Supported);
    }

    [Fact]
    public async Task Quic_Loopback_Handshake_Succeeds_When_Supported()
    {
        if (!QuicLink.Supported)
        {
            using var cert = OpaqueQuicClient.GenerateValidP256Certificate();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => QuicLink.Listen(IPAddress.Loopback, cert, 42461));
            Assert.Equal("quic-unsupported", ex.Message);
            return;
        }

        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=contextall");
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var endpoint = new IPEndPoint(IPAddress.Loopback, freePort);

        await using var listener = await QuicLink.Listen(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(endpoint);

        await using var serverConn = await acceptTask;
        Assert.NotNull(serverConn);
        Assert.NotNull(client.Connection);
    }

    [Fact]
    public async Task Quic_Negotiates_Alpn_ContextAll()
    {
        if (!QuicLink.Supported)
        {
            Assert.Equal("contextall", QuicLink.Alpn);
            return;
        }

        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=contextall");
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var endpoint = new IPEndPoint(IPAddress.Loopback, freePort);

        await using var listener = await QuicLink.Listen(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(endpoint, alpn: "contextall");

        await using var serverConn = await acceptTask;
        Assert.Equal("contextall", client.Connection!.NegotiatedApplicationProtocol.ToString());
    }

    [Fact]
    public async Task Quic_Opens_Single_Bidirectional_Stream()
    {
        if (!QuicLink.Supported)
        {
            Assert.Equal(42461, QuicLink.Port);
            return;
        }

        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=contextall");
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var endpoint = new IPEndPoint(IPAddress.Loopback, freePort);

        await using var listener = await QuicLink.Listen(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(endpoint);
        await using var serverConn = await acceptTask;

        await client.OpenSessionStreamAsync();
        Assert.NotNull(client.SessionStream);
        Assert.Equal(QuicStreamType.Bidirectional, client.SessionStream.Type);

        await using var serverStream = await serverConn.AcceptInboundStreamAsync();
        Assert.NotNull(serverStream);
        Assert.Equal(QuicStreamType.Bidirectional, serverStream.Type);
    }

    [Fact]
    public async Task Quic_Closes_Cleanly_Without_Error()
    {
        if (!QuicLink.Supported)
        {
            Assert.False(QuicLink.Supported);
            return;
        }

        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=contextall");
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var endpoint = new IPEndPoint(IPAddress.Loopback, freePort);

        await using var listener = await QuicLink.Listen(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(endpoint);
        await using var serverConn = await acceptTask;

        await client.Connection!.CloseAsync(0);
        await Task.Delay(50);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await client.Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        });
    }
}
