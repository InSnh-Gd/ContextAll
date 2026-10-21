using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ContextAll.Effects;

public static class QuicLink
{
    public const string Alpn = "contextall";

    public const int Port = 42461;

    public static bool Supported => QuicListener.IsSupported;

    public static QuicServerConnectionOptions ServerOptions(X509Certificate2 certificate) => new()
    {
        DefaultCloseErrorCode = 0,
        DefaultStreamErrorCode = 0,
        IdleTimeout = TimeSpan.FromSeconds(60),
        KeepAliveInterval = TimeSpan.FromSeconds(15),
        ServerAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ApplicationProtocols = [new SslApplicationProtocol(Alpn)],
            ServerCertificate = certificate,
        },
    };

    public static async Task<QuicListener> Listen(
        IPAddress address,
        X509Certificate2 certificate,
        int port = Port,
        CancellationToken cancellationToken = default)
    {
        if (!Supported)
        {
            throw new InvalidOperationException("quic-unsupported");
        }

        return await QuicListener.ListenAsync(
            new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(address, port),
                ApplicationProtocols = [new SslApplicationProtocol(Alpn)],
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(ServerOptions(certificate)),
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<QuicConnection> Connect(IPEndPoint remote, CancellationToken cancellationToken = default)
    {
        return await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                RemoteEndPoint = remote,
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                IdleTimeout = TimeSpan.FromSeconds(60),
                KeepAliveInterval = TimeSpan.FromSeconds(15),
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [new SslApplicationProtocol(Alpn)],
                    TargetHost = "contextall",
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static ValueTask<QuicStream> OpenSession(QuicConnection connection, CancellationToken cancellationToken = default) =>
        connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);

    public static ValueTask<QuicStream> AcceptSession(QuicConnection connection, CancellationToken cancellationToken = default) =>
        connection.AcceptInboundStreamAsync(cancellationToken);
}
