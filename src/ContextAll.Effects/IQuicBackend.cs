using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace ContextAll.Effects;

public interface IQuicBackend
{
    bool IsSupported { get; }

    Task<IQuicListener> ListenMutualAsync(
        IPAddress address,
        X509Certificate2 certificate,
        int port = QuicLink.Port,
        CancellationToken cancellationToken = default);

    Task<(IQuicConnection Connection, X509Certificate2 Peer)> ConnectMutualAsync(
        IPEndPoint remote,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default);
}

public interface IQuicListener : IAsyncDisposable
{
    IPEndPoint LocalEndPoint { get; }

    ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IQuicConnection : IAsyncDisposable
{
    EndPoint? RemoteEndPoint { get; }

    X509Certificate2? PeerCertificate { get; }

    ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken = default);

    ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken = default);
}

