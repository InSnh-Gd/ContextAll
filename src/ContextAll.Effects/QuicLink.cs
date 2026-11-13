using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace ContextAll.Effects;

public static class QuicLink
{
    public const string Alpn = "contextall";

    public const int Port = 42461;

    public static bool Supported => QuicListener.IsSupported;

    public static IReadOnlyList<IPEndPoint> ListenerEndpoints(IEnumerable<IPAddress> beaconAddresses)
    {
        var endpoints = new List<IPEndPoint>();
        foreach (var address in beaconAddresses)
        {
            if (endpoints.Exists(endpoint => endpoint.Address.Equals(address)))
            {
                continue;
            }

            endpoints.Add(new IPEndPoint(address, Port));
        }

        return endpoints;
    }

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

    public static Task<QuicConnection> Connect(IPEndPoint remote, CancellationToken cancellationToken = default) =>
        Connect(remote, 0, cancellationToken);

    public static async Task<QuicConnection> Connect(
        IPEndPoint remote,
        int maxInboundBidirectionalStreams,
        CancellationToken cancellationToken = default)
    {
        return await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                RemoteEndPoint = remote,
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
                IdleTimeout = TimeSpan.FromSeconds(60),
                KeepAliveInterval = TimeSpan.FromSeconds(15),
                MaxInboundBidirectionalStreams = maxInboundBidirectionalStreams,
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

    static readonly ConditionalWeakTable<QuicConnection, PeerCertificate> peerCertificates = new();

    public static async Task<QuicListener> ListenMutual(
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
                ConnectionOptionsCallback = (connection, _, _) =>
                {
                    var options = ServerOptions(certificate);
                    options.ServerAuthenticationOptions.ClientCertificateRequired = true;
                    options.ServerAuthenticationOptions.RemoteCertificateValidationCallback = (_, remote, _, _) =>
                    {
                        Remember(connection, remote);
                        return true;
                    };
                    return ValueTask.FromResult(options);
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(QuicConnection Connection, X509Certificate2 Peer)> ConnectMutual(
        IPEndPoint remote,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        X509Certificate2? peer = null;
        var connection = await QuicConnection.ConnectAsync(
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
                    ClientCertificates = [certificate],
                    TargetHost = "contextall",
                    RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                    {
                        peer = Copy(remoteCertificate);
                        return true;
                    },
                },
            },
            cancellationToken).ConfigureAwait(false);
        if (peer is null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("peer-certificate");
        }

        return (connection, peer);
    }

    public static bool TryPeerCertificate(QuicConnection connection, out X509Certificate2 certificate)
    {
        if (peerCertificates.TryGetValue(connection, out var box) && box.Certificate is not null)
        {
            certificate = box.Certificate;
            return true;
        }

        certificate = null!;
        return false;
    }

    static void Remember(QuicConnection connection, X509Certificate? certificate)
    {
        var copy = Copy(certificate);
        if (copy is null)
        {
            return;
        }

        peerCertificates.AddOrUpdate(connection, new PeerCertificate(copy));
    }

    static X509Certificate2? Copy(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return null;
        }

        return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
    }

    sealed class PeerCertificate(X509Certificate2 certificate)
    {
        public X509Certificate2 Certificate { get; } = certificate;
    }
}
