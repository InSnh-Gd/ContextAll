using System.Net;
using System.Security.Cryptography.X509Certificates;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B02")]
public sealed class B02_QuicUnsupportedNoFallbackTests
{
    [Fact]
    public async Task Quic_Throws_QuicUnsupported_When_Not_Supported()
    {
        using var cert = OpaqueQuicClient.GenerateValidP256Certificate();
        if (QuicLink.Supported)
        {
            var port = NetworkSandbox.GetFreePort();
            await using var listener = await QuicLink.Listen(IPAddress.Loopback, cert, port);
            Assert.NotNull(listener);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QuicLink.Listen(IPAddress.Loopback, cert, 42461));
            Assert.Equal("quic-unsupported", ex.Message);
        }
    }

    [Fact]
    public void Quic_Strictly_Prohibits_Tcp_And_WebSocket_Fallback()
    {
        var adr0004Path = Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/adr/0004-quic-android-transport.md");
        Assert.True(File.Exists(adr0004Path), "ADR 0004 must exist on disk");

        var content = File.ReadAllText(adr0004Path);
        Assert.Contains("不为 Android 端提供 TCP、WebSocket、HTTP 或本地套接字等降级传输方案", content);
        Assert.Contains("破坏与桌面守护进程间的协议等价性", content);
    }

    [Fact]
    public void Certificate_Rejects_Non_P256_Keys()
    {
        using var rsaCert = OpaqueQuicClient.GenerateRsaCertificate();
        var isP256 = CertificateChecks.IsP256(rsaCert);
        Assert.False(isP256);

        var spki = CertificateChecks.LeafSpki(rsaCert);
        Assert.Null(spki);

        var fate = CertificateChecks.Evaluate(rsaCert, null);
        Assert.Equal(CertificateFate.RejectBeforeId, fate);
    }

    [Fact]
    public void Certificate_Accepts_Valid_P256_Keys()
    {
        using var p256Cert = OpaqueQuicClient.GenerateValidP256Certificate();
        var isP256 = CertificateChecks.IsP256(p256Cert);
        Assert.True(isP256);

        var spki = CertificateChecks.LeafSpki(p256Cert);
        Assert.NotNull(spki);
        Assert.NotEmpty(spki);

        var fate = CertificateChecks.Evaluate(p256Cert, null);
        Assert.Equal(CertificateFate.Continue, fate);
    }

    [Fact]
    public void Quic_ServerOptions_Enforces_60s_IdleTimeout_And_15s_KeepAlive()
    {
        using var cert = OpaqueQuicClient.GenerateValidP256Certificate();
        var options = QuicLink.ServerOptions(cert);

        Assert.Equal(TimeSpan.FromSeconds(60), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), options.KeepAliveInterval);
    }
}
