using System.Net;
using System.Net.Sockets;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Host;

public sealed record MdnsPeer(string DeviceId, string? DisplayName, string Address, int Port);

public sealed class MdnsDiscovery : IDisposable
{
    public const int Port = 5353;

    internal const int QuerySeconds = 60;

    static readonly IPAddress Group4 = IPAddress.Parse("224.0.0.251");
    static readonly IPAddress Group6 = IPAddress.Parse("ff02::fb");

    readonly string deviceId;
    readonly string? displayName;
    readonly int quicPort;
    readonly Action<MdnsPeer> found;
    readonly Action<string> log;
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    readonly List<Socket> sockets = [];
    readonly List<Task> tasks = [];
    CancellationTokenSource? generation;
    IReadOnlyList<OnLinkPrefix> prefixes = [];
    bool warned;
    bool disposed;

    public MdnsDiscovery(string deviceId, string? displayName, int quicPort, Action<MdnsPeer> found, Action<string>? log = null)
    {
        this.deviceId = deviceId;
        this.displayName = displayName;
        this.quicPort = quicPort;
        this.found = found;
        this.log = log ?? (message => Console.Error.WriteLine(message));
    }

    internal bool Active
    {
        get
        {
            lock (gate)
            {
                return sockets.Count > 0;
            }
        }
    }

    public void Start(IReadOnlyList<OnLinkPrefix> localPrefixes)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            prefixes = localPrefixes;
            StartUnlocked();
        }
    }

    public void Rebuild(IReadOnlyList<OnLinkPrefix> localPrefixes)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            StopUnlocked();
            prefixes = localPrefixes;
            StartUnlocked();
        }
    }

    void StartUnlocked()
    {
        generation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var token = generation.Token;
        var v4 = Open(AddressFamily.InterNetwork);
        var v6 = Open(AddressFamily.InterNetworkV6);
        if (v4 is null && v6 is null)
        {
            return;
        }

        if (v4 is not null)
        {
            sockets.Add(v4);
            Join4(v4);
            tasks.Add(Task.Run(() => Receive(v4, token), token));
        }

        if (v6 is not null)
        {
            sockets.Add(v6);
            Join6(v6);
            tasks.Add(Task.Run(() => Receive(v6, token), token));
        }

        tasks.Add(Task.Run(() => QueryLoop(token), token));
    }

    Socket? Open(AddressFamily family)
    {
        try
        {
            var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            TryReusePort(socket);
            if (family == AddressFamily.InterNetworkV6)
            {
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));
            }
            else
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, Port));
            }

            return socket;
        }
        catch (SocketException ex)
        {
            Warn("mdns-skipped " + family + " " + ex.SocketErrorCode);
            return null;
        }
    }

    static void TryReusePort(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseUnicastPort, true);
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
        {
            // ReuseAddress still allows sharing on the platforms we support.
        }
    }

    void Join4(Socket socket)
    {
        foreach (var prefix in prefixes)
        {
            if (!IPAddress.TryParse(prefix.Address, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group4, address));
            }
            catch (SocketException ex)
            {
                Warn("mdns-join " + address + " " + ex.SocketErrorCode);
            }
        }
    }

    void Join6(Socket socket)
    {
        foreach (var prefix in prefixes)
        {
            if (prefix.InterfaceIndex <= 0 || !IPAddress.TryParse(prefix.Address, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                continue;
            }

            try
            {
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(Group6, (long)prefix.InterfaceIndex));
            }
            catch (SocketException ex)
            {
                Warn("mdns-join " + address + " " + ex.SocketErrorCode);
            }
        }
    }

    async Task Receive(Socket socket, CancellationToken token)
    {
        var buffer = new byte[9000];
        try
        {
            while (!token.IsCancellationRequested)
            {
                EndPoint remote = socket.AddressFamily == AddressFamily.InterNetwork
                    ? new IPEndPoint(IPAddress.Any, 0)
                    : new IPEndPoint(IPAddress.IPv6Any, 0);
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, token).ConfigureAwait(false);
                var source = ((IPEndPoint)result.RemoteEndPoint).Address;
                var read = MdnsCodec.Read(buffer.AsSpan(0, result.ReceivedBytes), source);
                if (read.ServiceQuery)
                {
                    SendResponse(socket, result.RemoteEndPoint, source);
                }

                foreach (var service in read.Services)
                {
                    found(new MdnsPeer(service.DeviceId, service.DisplayName, LocalNetwork.Text(service.Address), service.Port));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The discovery instance stopped.
        }
    }

    void SendResponse(Socket socket, EndPoint remote, IPAddress source)
    {
        var address = AdvertisedAddress(prefixes, source.AddressFamily);
        byte[] payload;
        try
        {
            payload = MdnsCodec.EncodeResponse(deviceId, displayName, quicPort, address);
        }
        catch (ArgumentException)
        {
            payload = MdnsCodec.EncodeResponse(deviceId, null, quicPort, address);
        }

        try
        {
            socket.SendTo(payload, remote);
        }
        catch (SocketException)
        {
            // The requester left.
        }
    }

    async Task QueryLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                SendQuery();
                await Task.Delay(TimeSpan.FromSeconds(QuerySeconds), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The discovery instance stopped.
        }
    }

    void SendQuery()
    {
        var query = MdnsCodec.EncodeQuery(0);
        foreach (var socket in sockets.ToArray())
        {
            try
            {
                if (socket.AddressFamily == AddressFamily.InterNetwork)
                {
                    socket.SendTo(query, new IPEndPoint(Group4, Port));
                }
                else
                {
                    foreach (var index in prefixes.Where(prefix => prefix.InterfaceIndex > 0).Select(prefix => prefix.InterfaceIndex).Distinct())
                    {
                        socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, index);
                        socket.SendTo(query, new IPEndPoint(Group6, Port));
                    }
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // This interface is not up, or the socket was rebuilt.
            }
        }
    }

    internal static IPAddress? AdvertisedAddress(IReadOnlyList<OnLinkPrefix> prefixes, AddressFamily family) =>
        prefixes
            .Where(prefix => !prefix.LinkLocal)
            .Select(prefix => IPAddress.TryParse(prefix.Address, out var parsed) ? parsed : null)
            .FirstOrDefault(parsed => parsed is not null && parsed.AddressFamily == family);

    void Warn(string message)
    {
        if (warned)
        {
            return;
        }

        warned = true;
        log(message);
    }

    void StopUnlocked()
    {
        generation?.Cancel();
        generation?.Dispose();
        generation = null;
        foreach (var socket in sockets)
        {
            socket.Dispose();
        }

        sockets.Clear();
        tasks.Clear();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopUnlocked();
            stop.Cancel();
            stop.Dispose();
        }
    }
}
