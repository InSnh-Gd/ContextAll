using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace ContextAll.Effects;

public static class QuicLink
{
    public const string Alpn = "contextall";

    public const int Port = 42461;

    static IQuicBackend backend = SystemNetQuicBackend.Instance;

    public static IQuicBackend Backend => Volatile.Read(ref backend);

    public static void UseBackend(IQuicBackend customBackend)
    {
        ArgumentNullException.ThrowIfNull(customBackend);
        Volatile.Write(ref backend, customBackend);
    }

    public static bool Supported => Backend.IsSupported;

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

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
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

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static async Task<QuicListener> Listen(
        IPAddress address,
        X509Certificate2 certificate,
        int port = Port,
        CancellationToken cancellationToken = default)
    {
        if (!QuicListener.IsSupported)
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

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static Task<QuicConnection> Connect(IPEndPoint remote, CancellationToken cancellationToken = default) =>
        Connect(remote, 0, cancellationToken);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
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

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static ValueTask<QuicStream> OpenSession(QuicConnection connection, CancellationToken cancellationToken = default) =>
        connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static ValueTask<QuicStream> AcceptSession(QuicConnection connection, CancellationToken cancellationToken = default) =>
        connection.AcceptInboundStreamAsync(cancellationToken);

    public static ValueTask<Stream> OpenSession(IQuicConnection connection, CancellationToken cancellationToken = default) =>
        connection.OpenOutboundStreamAsync(cancellationToken);

    public static ValueTask<Stream> AcceptSession(IQuicConnection connection, CancellationToken cancellationToken = default) =>
        connection.AcceptInboundStreamAsync(cancellationToken);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    static readonly ConditionalWeakTable<QuicConnection, PeerCertificate> peerCertificates = new();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static async Task<QuicListener> ListenMutual(
        IPAddress address,
        X509Certificate2 certificate,
        int port = Port,
        CancellationToken cancellationToken = default)
    {
        if (!QuicListener.IsSupported)
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

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static async Task<(QuicConnection Connection, X509Certificate2 Peer)> ConnectMutual(
        IPEndPoint remote,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        if (!QuicConnection.IsSupported)
        {
            throw new InvalidOperationException("quic-unsupported");
        }
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

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static bool TryPeerCertificate(QuicConnection connection, [MaybeNullWhen(false)] out X509Certificate2 certificate)
    {
        if (peerCertificates.TryGetValue(connection, out var box) && box.Certificate is not null)
        {
            certificate = box.Certificate;
            return true;
        }

        certificate = null;
        return false;
    }

    public static bool TryPeerCertificate(IQuicConnection connection, [MaybeNullWhen(false)] out X509Certificate2 certificate)
    {
        if (connection.PeerCertificate is { } peer)
        {
            certificate = peer;
            return true;
        }

        certificate = null;
        return false;
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static IQuicListener WrapListener(QuicListener listener) => new SystemNetQuicListenerAdapter(listener);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    public static IQuicConnection WrapConnection(QuicConnection connection, X509Certificate2? peer = null) =>
        new SystemNetQuicConnectionAdapter(connection, peer);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
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

    sealed class SystemNetQuicBackend : IQuicBackend
    {
        public static readonly SystemNetQuicBackend Instance = new();

        public bool IsSupported =>
            (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            && QuicListener.IsSupported;

        public async Task<IQuicListener> ListenMutualAsync(
            IPAddress address,
            X509Certificate2 certificate,
            int port = Port,
            CancellationToken cancellationToken = default)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                var listener = await ListenMutual(address, certificate, port, cancellationToken).ConfigureAwait(false);
                return new SystemNetQuicListenerAdapter(listener);
            }

            throw new InvalidOperationException("quic-unsupported");
        }

        public async Task<(IQuicConnection Connection, X509Certificate2 Peer)> ConnectMutualAsync(
            IPEndPoint remote,
            X509Certificate2 certificate,
            CancellationToken cancellationToken = default)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                var (connection, peer) = await ConnectMutual(remote, certificate, cancellationToken).ConfigureAwait(false);
                return (new SystemNetQuicConnectionAdapter(connection, peer), peer);
            }

            throw new InvalidOperationException("quic-unsupported");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    sealed class SystemNetQuicListenerAdapter(QuicListener inner) : IQuicListener
    {
        public IPEndPoint LocalEndPoint => inner.LocalEndPoint;

        public async ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = await inner.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
            TryPeerCertificate(connection, out var peer);
            return new SystemNetQuicConnectionAdapter(connection, peer);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("macos")]
    sealed class SystemNetQuicConnectionAdapter(QuicConnection inner, X509Certificate2? peer) : IQuicConnection
    {
        public QuicConnection Inner => inner;

        public EndPoint? RemoteEndPoint => inner.RemoteEndPoint;

        public X509Certificate2? PeerCertificate => peer ?? (TryPeerCertificate(inner, out var found) ? found : null);

        public async ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken = default) =>
            await OpenSession(inner, cancellationToken).ConfigureAwait(false);

        public async ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken = default) =>
            await AcceptSession(inner, cancellationToken).ConfigureAwait(false);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
