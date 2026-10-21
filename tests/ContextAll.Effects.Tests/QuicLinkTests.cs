using System.Net;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ContextAll.Effects;

namespace ContextAll.Effects.Tests;

public sealed class QuicLinkTests
{
    [Fact]
    public async Task The_initiator_opens_one_bidirectional_stream_on_port_42461()
    {
        using var certificate = Certificate();
        if (!QuicLink.Supported)
        {
            var error = await Record.ExceptionAsync(() => QuicLink.Listen(IPAddress.Loopback, certificate));
            var thrown = Assert.IsType<InvalidOperationException>(error);
            Assert.Equal("quic-unsupported", thrown.Message);
            return;
        }

        await using var listener = await QuicLink.Listen(IPAddress.Loopback, certificate);
        Assert.Equal(QuicLink.Port, ((IPEndPoint)listener.LocalEndPoint).Port);
        var accepted = listener.AcceptConnectionAsync();
        await using var client = await QuicLink.Connect(new IPEndPoint(IPAddress.Loopback, QuicLink.Port));
        await using var outbound = await QuicLink.OpenSession(client);
        await outbound.WriteAsync(new byte[] { 1 });
        await using var server = await accepted;
        await using var inbound = await QuicLink.AcceptSession(server);
        var buffer = new byte[1];
        Assert.Equal(1, await inbound.ReadAsync(buffer));
        using var second = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.AcceptInboundStreamAsync(second.Token).AsTask());
    }

    static X509Certificate2 Certificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256);
        var signed = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);
    }
}
