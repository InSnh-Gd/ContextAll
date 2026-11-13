using System.Net;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F16")]
public sealed class F16_InProcessQuicTransportTests
{
    [Fact]
    public void Quic_Default_Port_Is_42461()
    {
        Assert.Equal(42461, QuicLink.Port);
    }

    [Fact]
    public void Quic_Default_Alpn_Is_ContextAll()
    {
        Assert.Equal("contextall", QuicLink.Alpn);
    }

    [Fact]
    public async Task InProcess_Quic_Loopback_Transmits_16Byte_Framed_Message()
    {
        if (!QuicLink.Supported)
        {
            using var serverCertUnsup = OpaqueQuicClient.GenerateValidP256Certificate();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QuicLink.Listen(IPAddress.Loopback, serverCertUnsup, 42461));
            Assert.Equal("quic-unsupported", ex.Message);
            return;
        }

        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate();
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var endpoint = new IPEndPoint(IPAddress.Loopback, freePort);

        await using var listener = await QuicLink.Listen(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        await using var client = new OpaqueQuicClient();
        await client.ConnectAsync(endpoint);
        await using var serverConn = await acceptTask;

        await client.OpenSessionStreamAsync();
        await using var serverStream = await serverConn.AcceptInboundStreamAsync();

        var payload = "{\"intent\":\"pairing\",\"displayName\":\"AndroidNode\"}"u8.ToArray();
        await client.SendRawFrameAsync(FrameTypes.Hello, 0, 0, 0, payload);

        var header = new byte[FrameLimits.HeaderBytes];
        var readHeader = await serverStream.ReadAsync(header);
        Assert.Equal(FrameLimits.HeaderBytes, readHeader);

        var frameRead = FrameCodec.Read(header);
        Assert.True(frameRead.NeedMore);
    }

    [Fact]
    public void InProcess_Session_Zero_Unix_Sockets_Audit()
    {
        var isClean = InProcessServiceHarness.AuditZeroUnixSockets("contextall-inprocess-test");
        Assert.True(isClean);
    }

    [Fact]
    public async Task InProcess_Quic_Supports_Mutual_Tls_With_Client_Certificate()
    {
        if (!QuicLink.Supported)
        {
            using var serverCertUnsup = OpaqueQuicClient.GenerateValidP256Certificate();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QuicLink.ListenMutual(IPAddress.Loopback, serverCertUnsup, 42461));
            Assert.Equal("quic-unsupported", ex.Message);
            return;
        }

        using var serverCert = OpaqueQuicClient.GenerateValidP256Certificate();
        using var clientCert = OpaqueQuicClient.GenerateValidP256Certificate();
        var freePort = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        var endpoint = new IPEndPoint(IPAddress.Loopback, freePort);

        await using var listener = await QuicLink.ListenMutual(endpoint.Address, serverCert, endpoint.Port);
        var acceptTask = listener.AcceptConnectionAsync().AsTask();

        await using var client = new OpaqueQuicClient(clientCert);
        await client.ConnectAsync(endpoint);
        await using var serverConn = await acceptTask;

        Assert.True(QuicLink.TryPeerCertificate(serverConn, out var peerCert));
        Assert.NotNull(peerCert);
    }
}
