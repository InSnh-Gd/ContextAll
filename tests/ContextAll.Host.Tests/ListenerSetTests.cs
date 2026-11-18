using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ContextAll.Effects;

namespace ContextAll.Host.Tests;

public sealed class ListenerSetTests
{
    [Fact]
    public async Task An_aborted_handshake_does_not_stop_accepting_connections()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new InterruptedListener();
        await using var listeners = new ListenerSet(
            (_, _) => Task.FromResult<IQuicListener>(listener),
            (_, _) => Task.CompletedTask,
            _ => { }, timeout.Token);
        await listeners.RebuildAsync([IPAddress.Loopback]);
        await listener.SecondAccept.Task.WaitAsync(timeout.Token);
    }

    sealed class InterruptedListener : IQuicListener
    {
        int count;
        public IPEndPoint LocalEndPoint => new(IPAddress.Loopback, 42461);
        public TaskCompletionSource SecondAccept { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref count);
            if (attempt == 1) throw new QuicException(QuicError.ConnectionAborted, null, "handshake interrupted");
            if (attempt == 2) throw new AuthenticationException("handshake failed");
            SecondAccept.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact(Timeout = 30000)]
    public async Task A_mutual_listener_accepts_one_stream()
    {
        if (!QuicLink.Supported)
        {
            return;
        }

        using var certificate = Certificate();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var loopback = IPAddress.Loopback;
        var port = FreePort(loopback);
        await using var listener = await QuicLink.ListenMutual(loopback, certificate, port, stop.Token);
        var accepting = listener.AcceptConnectionAsync(stop.Token).AsTask();
        var (connection, _) = await QuicLink.ConnectMutual(new IPEndPoint(loopback, port), certificate, stop.Token);
        await using var clientConnection = connection;
        var serverConnection = await accepting;
        await using var serverConnectionGuard = serverConnection;
        await using var client = await QuicLink.OpenSession(connection, stop.Token);
        await client.WriteAsync(new byte[] { 1 }, stop.Token);
        await using var server = await QuicLink.AcceptSession(serverConnection, stop.Token);
        var first = new byte[1];
        Assert.Equal(1, await server.ReadAsync(first, stop.Token));
        Assert.Equal(1, first[0]);

        await RoundTrip(client, server, stop.Token);
    }

    [Fact(Timeout = 30000)]
    public async Task A_live_session_survives_adding_and_removing_another_address()
    {
        if (!QuicLink.Supported)
        {
            return;
        }

        using var certificate = Certificate();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var loopback = IPAddress.Loopback;
        var added = IPAddress.Parse("127.0.0.2");
        var port = FreePort(loopback);
        var acceptedStream = new TaskCompletionSource<QuicStream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = new List<QuicConnection>();

        await using var set = new ListenerSet(
            (address, token) => Listen(address, certificate, port, token),
            async (connection, token) =>
            {
                try
                {
                    var stream = await QuicLink.AcceptSession(connection, token);
                    lock (connections)
                    {
                        connections.Add(connection);
                    }

                    acceptedStream.TrySetResult(stream);
                }
                catch (Exception ex)
                {
                    acceptedStream.TrySetException(ex);
                }
            },
            _ => { },
            stop.Token);

        await set.RebuildAsync([loopback, added]);
        Assert.Equal(2, set.Count);

        var (connection, _) = await QuicLink.ConnectMutual(new IPEndPoint(loopback, port), certificate, stop.Token);
        await using var clientConnection = connection;
        await using var client = await QuicLink.OpenSession(connection, stop.Token);
        await client.WriteAsync(new byte[] { 7 }, stop.Token);
        await using var server = await acceptedStream.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = new byte[1];
        Assert.Equal(1, await server.ReadAsync(first, stop.Token));
        Assert.Equal(7, first[0]);

        await set.RebuildAsync([loopback]);
        Assert.Equal(1, set.Count);
        await RoundTrip(client, server, stop.Token);

        await set.RebuildAsync([]);
        Assert.Equal(0, set.Count);
        await RoundTrip(client, server, stop.Token);

        foreach (var item in connections)
        {
            await item.DisposeAsync();
        }
    }

    static int FreePort(IPAddress address)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(address, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    static Task<QuicListener> Listen(IPAddress address, X509Certificate2 certificate, int port, CancellationToken token) =>
        QuicLink.ListenMutual(address, certificate, port, token);

    static async Task RoundTrip(QuicStream client, QuicStream server, CancellationToken token)
    {
        var value = (byte)Random.Shared.Next(1, 255);
        await client.WriteAsync(new[] { value }, token);
        var buffer = new byte[1];
        Assert.Equal(1, await server.ReadAsync(buffer, token));
        Assert.Equal(value, buffer[0]);
    }

    static X509Certificate2 Certificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256);
        var signed = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pfx), null);
    }
}
