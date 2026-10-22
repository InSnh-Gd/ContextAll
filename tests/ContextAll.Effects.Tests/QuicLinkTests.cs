using System.Net;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ContextAll.Effects;

namespace ContextAll.Effects.Tests;

public sealed class QuicLinkTests
{
    [Fact]
    public void Beacon_addresses_map_to_one_endpoint_each_on_port_42461()
    {
        var first = IPAddress.Parse("10.50.0.1");
        var second = IPAddress.Parse("10.50.0.2");
        var endpoints = QuicLink.ListenerEndpoints([first, first, second]);
        Assert.Equal([new IPEndPoint(first, QuicLink.Port), new IPEndPoint(second, QuicLink.Port)], endpoints);
        Assert.Equal("contextall", QuicLink.Alpn);
    }

    [Fact]
    public async Task The_initiator_opens_one_bidirectional_stream_on_the_beacon_address()
    {
        using var certificate = Certificate();
        var beacon = IPAddress.Loopback;
        var endpoint = Assert.Single(QuicLink.ListenerEndpoints([beacon]));
        Assert.Equal(QuicLink.Port, endpoint.Port);
        if (!QuicLink.Supported)
        {
            var error = await Record.ExceptionAsync(() => QuicLink.Listen(endpoint.Address, certificate, endpoint.Port));
            var thrown = Assert.IsType<InvalidOperationException>(error);
            Assert.Equal("quic-unsupported", thrown.Message);
            return;
        }

        var serverOptions = QuicLink.ServerOptions(certificate);
        if (serverOptions.ServerAuthenticationOptions?.ApplicationProtocols is not { Count: > 0 } protocols)
        {
            throw new InvalidOperationException("alpn");
        }

        Assert.Equal(QuicLink.Alpn, protocols[0].ToString());
        await using var listener = await QuicLink.Listen(endpoint.Address, certificate, endpoint.Port);
        Assert.Equal(endpoint, listener.LocalEndPoint);
        var accepted = listener.AcceptConnectionAsync();
        await using var client = await QuicLink.Connect(endpoint, 1);
        Assert.Equal(QuicLink.Alpn, client.NegotiatedApplicationProtocol.ToString());
        await using var outbound = await QuicLink.OpenSession(client);
        await outbound.WriteAsync(new byte[] { 1 });
        await using var server = await accepted;
        await using var inbound = await QuicLink.AcceptSession(server);
        var buffer = new byte[1];
        Assert.Equal(1, await inbound.ReadAsync(buffer));
        Assert.Equal(QuicStreamType.Bidirectional, outbound.Type);
        using var fromClient = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.AcceptInboundStreamAsync(fromClient.Token).AsTask());
        using var fromServer = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AcceptInboundStreamAsync(fromServer.Token).AsTask());
    }

    static X509Certificate2 Certificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256);
        var signed = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);
    }
}
