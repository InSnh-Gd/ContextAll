using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ContextAll.Effects;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Harness;

public sealed class OpaqueQuicClient : IAsyncDisposable
{
    public const string DefaultAlpn = "contextall";
    public const int DefaultPort = 42461;

    public X509Certificate2 ClientCertificate { get; }
    public X509Certificate2? PeerCertificate { get; private set; }
    public QuicConnection? Connection { get; private set; }
    public QuicStream? SessionStream { get; private set; }

    readonly byte[] readBuffer = new byte[128 * 1024];
    int bufferedBytes;

    public OpaqueQuicClient(X509Certificate2? customCert = null)
    {
        ClientCertificate = customCert ?? GenerateValidP256Certificate("CN=contextall");
    }

    public static X509Certificate2 GenerateValidP256Certificate(string subject = "CN=contextall")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        var signed = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);
    }

    public static X509Certificate2 GenerateRsaCertificate(string subject = "CN=contextall")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signed = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);
    }

    public async Task ConnectAsync(
        IPEndPoint target,
        string alpn = DefaultAlpn,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        X509Certificate2? remoteCert = null;

        Connection = await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                RemoteEndPoint = target,
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                IdleTimeout = TimeSpan.FromSeconds(60),
                KeepAliveInterval = TimeSpan.FromSeconds(15),
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol(alpn)],
                ClientCertificates = [ClientCertificate],
                TargetHost = "contextall",
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert is not null)
                    {
                        remoteCert = new X509Certificate2(cert);
                    }
                    return true;
                },
            },
        },
        cts.Token).ConfigureAwait(false);

        PeerCertificate = remoteCert;
    }

    public async Task OpenSessionStreamAsync(CancellationToken ct = default)
    {
        if (Connection is null)
        {
            throw new InvalidOperationException("Not connected");
        }

        SessionStream = await Connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct).ConfigureAwait(false);
    }

    public async Task SendRawFrameAsync(
        byte type,
        byte priority,
        ushort flags,
        uint streamId,
        ReadOnlyMemory<byte> payload,
        uint reserved = 0,
        CancellationToken ct = default)
    {
        if (SessionStream is null)
        {
            throw new InvalidOperationException("No open stream");
        }

        var header = new byte[FrameLimits.HeaderBytes];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        header[4] = type;
        header[5] = priority;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), flags);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), streamId);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), reserved);

        await SessionStream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await SessionStream.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        await SessionStream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task<Frame?> ReadFrameAsync(CancellationToken ct = default)
    {
        if (SessionStream is null)
        {
            throw new InvalidOperationException("No open stream");
        }

        while (true)
        {
            if (bufferedBytes >= FrameLimits.HeaderBytes)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(readBuffer.AsSpan(0, 4));
                var total = FrameLimits.HeaderBytes + (int)length;

                if (bufferedBytes >= total)
                {
                    var read = FrameCodec.Read(readBuffer.AsSpan(0, total));
                    if (read.IsFailure)
                    {
                        throw new InvalidOperationException("Frame decode failure: " + read.Reason);
                    }

                    var remaining = bufferedBytes - total;
                    if (remaining > 0)
                    {
                        Array.Copy(readBuffer, total, readBuffer, 0, remaining);
                    }
                    bufferedBytes = remaining;
                    return read.Frame;
                }
            }

            var readCount = await SessionStream.ReadAsync(
                readBuffer.AsMemory(bufferedBytes, readBuffer.Length - bufferedBytes),
                ct).ConfigureAwait(false);

            if (readCount == 0)
            {
                return null;
            }

            bufferedBytes += readCount;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (SessionStream is not null)
        {
            await SessionStream.DisposeAsync().ConfigureAwait(false);
        }

        if (Connection is not null)
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }

        ClientCertificate.Dispose();
        PeerCertificate?.Dispose();
    }
}
