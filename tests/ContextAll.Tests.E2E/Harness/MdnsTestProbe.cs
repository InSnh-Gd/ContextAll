using System.Net;
using System.Net.Sockets;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Harness;

public sealed class PortOccupant : IDisposable
{
    readonly Socket? socket;
    readonly bool isAlreadyInUse;

    public bool IsOccupied => socket is not null || isAlreadyInUse;
    public bool Occupied => IsOccupied;
    public int Port { get; }

    internal PortOccupant(int port)
    {
        Port = port;
        try
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            s.Bind(new IPEndPoint(IPAddress.Any, port));
            socket = s;
        }
        catch (SocketException)
        {
            // Port already in use by system daemon (adb, avahi-daemon or systemd-resolved)
            socket = null;
            isAlreadyInUse = true;
        }
    }

    public void Dispose()
    {
        if (socket is not null)
        {
            try { socket.Close(); } catch { }
            socket.Dispose();
        }
    }
}

public sealed class MdnsTestProbe : IDisposable
{
    public const int DefaultPort = 5353;
    public const string MulticastGroupV4 = "224.0.0.251";

    readonly Socket socket;
    readonly List<MdnsService> capturedServices = [];
    readonly object sync = new();

    public int Port { get; }
    public IReadOnlyList<MdnsService> CapturedServices
    {
        get
        {
            lock (sync)
            {
                return [.. capturedServices];
            }
        }
    }

    public MdnsTestProbe(int port = DefaultPort, IPAddress? bindAddress = null)
    {
        Port = port;
        bindAddress ??= IPAddress.Any;

        socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(bindAddress, port));

        if (bindAddress.Equals(IPAddress.Any))
        {
            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(IPAddress.Parse(MulticastGroupV4)));
            }
            catch
            {
                // Multicast membership might fail if interface lacks multicast
            }
        }
    }

    public async Task SendQueryAsync(ushort queryId = 1, IPEndPoint? target = null, CancellationToken ct = default)
    {
        target ??= new IPEndPoint(IPAddress.Parse(MulticastGroupV4), DefaultPort);
        var query = MdnsCodec.EncodeQuery(queryId);
        await socket.SendToAsync(query, SocketFlags.None, target, ct).ConfigureAwait(false);
    }

    public async Task<MdnsService?> WaitForResponseAsync(TimeSpan timeout, Func<MdnsService, bool>? predicate = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var buffer = new byte[4096];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, cts.Token).ConfigureAwait(false);
                var fromAddress = ((IPEndPoint)result.RemoteEndPoint).Address;
                var read = MdnsCodec.Read(buffer.AsSpan(0, result.ReceivedBytes), fromAddress);

                foreach (var service in read.Services)
                {
                    lock (sync)
                    {
                        capturedServices.Add(service);
                    }

                    if (predicate is null || predicate(service))
                    {
                        return service;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    public static PortOccupant TryOccupyPort(int port = DefaultPort) => new(port);
    public static PortOccupant OccupyPort(int port = DefaultPort) => new(port);

    public void Dispose()
    {
        try { socket.Close(); } catch { }
        socket.Dispose();
    }
}
