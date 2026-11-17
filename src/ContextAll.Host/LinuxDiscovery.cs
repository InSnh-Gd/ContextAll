using System.Net;
using System.Net.Sockets;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class LinuxDiscovery : IDiscovery
{
    readonly string deviceId;
    readonly byte[] digest;
    readonly ushort port;
    string? displayName;
    readonly Action<string>? log;
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    readonly List<Socket> beaconSockets = [];
    MdnsDiscovery? mdns;
    bool started;
    bool disposed;

    public LinuxDiscovery(
        string deviceId,
        byte[] digest,
        string? displayName,
        ushort port,
        Action<string>? log = null)
    {
        this.deviceId = deviceId;
        this.digest = digest;
        this.displayName = displayName;
        this.port = port;
        this.log = log;
    }

    public bool IsActive
    {
        get
        {
            lock (gate) return started && !disposed;
        }
    }

    public event Action<DiscoveredPeer>? PeerDiscovered;

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (gate)
        {
            if (started || disposed) return Task.CompletedTask;
            started = true;

            var localPrefixes = LocalNetwork.Enumerate();
            mdns = new MdnsDiscovery(deviceId, displayName, port, OnMdnsFound, log);
            mdns.Start(localPrefixes);

            var v4 = ListenBeacon(AddressFamily.InterNetwork, IPAddress.Any);
            if (v4 is not null) beaconSockets.Add(v4);
            var v6 = ListenBeacon(AddressFamily.InterNetworkV6, IPAddress.IPv6Any);
            if (v6 is not null) beaconSockets.Add(v6);

            foreach (var s in beaconSockets)
            {
                _ = Task.Run(() => BeaconReceiveLoop(s, stop.Token), stop.Token);
            }
        }

        return Task.CompletedTask;
    }

    public void UpdateDisplayName(string newDisplayName)
    {
        lock (gate)
        {
            displayName = newDisplayName;
        }
    }

    public Task AnnounceAsync(ushort port, CancellationToken ct = default)
    {
        string? name;
        lock (gate)
        {
            name = displayName;
        }

        var outcome = BeaconCodec.Encode(digest, port, name ?? "ContextAll");
        if (outcome.IsFailure || outcome.Value is null) return Task.CompletedTask;
        var beaconBytes = outcome.Value;
        var localPrefixes = LocalNetwork.Enumerate();
        foreach (var prefix in localPrefixes)
        {
            if (IPAddress.TryParse(prefix.Address, out var ip))
            {
                try
                {
                    using var udp = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                    udp.EnableBroadcast = true;
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var broadcast = Broadcast(ip, prefix.PrefixLength);
                        udp.SendTo(beaconBytes, new IPEndPoint(broadcast, SessionHost.BeaconPort));
                    }
                    else if (ip.AddressFamily == AddressFamily.InterNetworkV6 && prefix.InterfaceIndex > 0)
                    {
                        udp.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, prefix.InterfaceIndex);
                        udp.SendTo(beaconBytes, new IPEndPoint(IPAddress.Parse("ff02::1"), SessionHost.BeaconPort));
                    }
                }
                catch
                {
                    // Ignore broadcast send errors
                }
            }
        }
        return Task.CompletedTask;
    }

    public void Rebuild(IReadOnlyList<OnLinkPrefix> prefixes)
    {
        lock (gate)
        {
            mdns?.Rebuild(prefixes);
        }
    }

    void OnMdnsFound(MdnsPeer peer)
    {
        if (peer.DeviceId == deviceId) return;
        PeerDiscovered?.Invoke(new DiscoveredPeer(peer.DeviceId, peer.Address, peer.Port, peer.DisplayName));
    }

    static Socket? ListenBeacon(AddressFamily family, IPAddress any)
    {
        try
        {
            var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            socket.EnableBroadcast = true;
            if (family == AddressFamily.InterNetworkV6)
            {
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            }
            socket.Bind(new IPEndPoint(any, SessionHost.BeaconPort));
            return socket;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    async Task BeaconReceiveLoop(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[1024];
        EndPoint remote = socket.AddressFamily == AddressFamily.InterNetwork
            ? new IPEndPoint(IPAddress.Any, 0)
            : new IPEndPoint(IPAddress.IPv6Any, 0);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, ct).ConfigureAwait(false);
                var outcome = BeaconCodec.Decode(buffer.AsSpan(0, result.ReceivedBytes));
                if (outcome.IsFailure || outcome.Value is null) continue;

                var beacon = outcome.Value;
                var remoteIp = ((IPEndPoint)result.RemoteEndPoint).Address;
                var remoteAddress = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4().ToString() : remoteIp.ToString();
                var devId = DeviceIds.Text(beacon.Digest);

                if (devId == deviceId || beacon.Digest.AsSpan().SequenceEqual(digest)) continue;

                PeerDiscovered?.Invoke(new DiscoveredPeer(devId, beacon.Digest, remoteAddress, beacon.Port, beacon.Name));
            }
        }
        catch
        {
            // Socket closed or canceled
        }
    }

    static IPAddress Broadcast(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        var bits = prefix;
        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(bits, 0, 8);
            var mask = keep == 0 ? (byte)0 : (byte)(0xFF << (8 - keep));
            bytes[i] = (byte)(bytes[i] | (byte)~mask);
            bits -= keep;
        }
        return new IPAddress(bytes);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stop.Cancel();
            stop.Dispose();
            mdns?.Dispose();
            foreach (var s in beaconSockets) s.Dispose();
            beaconSockets.Clear();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
