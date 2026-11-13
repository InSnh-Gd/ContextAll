using System.Net;
using System.Text;
using Android.Content;
using Android.Net;
using Android.Net.Nsd;
using Android.Net.Wifi;
using Android.OS;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;
using Java.Net;

namespace ContextAll.App.Android.Platform;

public sealed record AndroidNetworkDescriptor(
    string Id,
    bool HasWifi,
    bool HasEthernet,
    bool HasCellular,
    bool HasVpn,
    IReadOnlyList<OnLinkPrefix> Prefixes,
    Network? PlatformNetwork = null);

public sealed class AndroidDiscovery : IDiscovery
{
    public const string NsdServiceType = "_contextall._udp";

    readonly Context context;
    readonly string deviceId;
    readonly byte[] digest;
    readonly int beaconPort;
    readonly ushort quicPort;
    readonly bool forceNsdFailure;
    readonly Action<string>? log;
    readonly object gate = new();
    readonly CancellationTokenSource stop = new();
    readonly List<DatagramSocket> receiveSockets = [];
    string displayName;
    ConnectivityManager? connectivityManager;
    LanNetworkCallback? networkCallback;
    WifiManager.MulticastLock? multicastLock;
    NsdManager? nsdManager;
    RegistrationHandler? registrationListener;
    DiscoveryHandler? discoveryListener;
    IReadOnlyList<AndroidNetworkDescriptor>? overrideNetworks;
    List<AndroidNetworkDescriptor> selectedNetworks = [];
    bool nsdRegistered;
    bool nsdBrowsing;
    bool nsdDegraded;
    bool started;
    bool disposed;

    public AndroidDiscovery(
        Context context,
        string deviceId,
        byte[] digest,
        string displayName,
        ushort quicPort = QuicLink.Port,
        int beaconPort = SessionHost.BeaconPort,
        bool forceNsdFailure = false,
        IReadOnlyList<AndroidNetworkDescriptor>? overrideNetworks = null,
        Action<string>? log = null)
    {
        this.context = context;
        this.deviceId = deviceId;
        this.digest = digest.ToArray();
        this.displayName = string.IsNullOrWhiteSpace(displayName) ? "ContextAll" : displayName;
        this.quicPort = quicPort;
        this.beaconPort = beaconPort;
        this.forceNsdFailure = forceNsdFailure;
        this.overrideNetworks = overrideNetworks;
        this.log = log;
    }

    public bool IsActive
    {
        get
        {
            lock (gate)
            {
                return started && !disposed;
            }
        }
    }

    public bool OwnsNetworkAndSockets => true;

    public bool IsNsdRegistered
    {
        get
        {
            lock (gate)
            {
                return nsdRegistered;
            }
        }
    }

    public bool IsNsdDegraded
    {
        get
        {
            lock (gate)
            {
                return nsdDegraded;
            }
        }
    }

    public bool HasMulticastLock
    {
        get
        {
            lock (gate)
            {
                return multicastLock is { IsHeld: true };
            }
        }
    }

    public int ReceiveSocketCount
    {
        get
        {
            lock (gate)
            {
                return receiveSockets.Count;
            }
        }
    }

    public event Action? NetworkChanged;

    public event Action<DiscoveredPeer>? PeerDiscovered;

    public static bool IsEligibleNetwork(bool hasWifi, bool hasEthernet, bool hasCellular, bool hasVpn)
    {
        _ = hasCellular;
        return !hasVpn && (hasWifi || hasEthernet);
    }

    public static IReadOnlyList<AndroidNetworkDescriptor> FilterEligibleNetworks(IEnumerable<AndroidNetworkDescriptor> candidates) =>
        candidates
            .Where(n => IsEligibleNetwork(n.HasWifi, n.HasEthernet, n.HasCellular, n.HasVpn))
            .ToArray();

    public IReadOnlyList<AndroidNetworkDescriptor> SelectedNetworksSnapshot()
    {
        RefreshSelectedNetworks();
        lock (gate)
        {
            return selectedNetworks.ToArray();
        }
    }

    public IReadOnlyList<OnLinkPrefix>? GetActivePrefixes()
    {
        RefreshSelectedNetworks();
        lock (gate)
        {
            return selectedNetworks
                .SelectMany(n => n.Prefixes)
                .DistinctBy(p => $"{p.Address}/{p.PrefixLength}/{p.InterfaceIndex}")
                .ToArray();
        }
    }

    public void SetOverrideNetworks(IReadOnlyList<AndroidNetworkDescriptor>? networks)
    {
        lock (gate)
        {
            overrideNetworks = networks;
        }

        RefreshSelectedNetworks();
        RebindReceiveSockets();
        NetworkChanged?.Invoke();
    }

    public void UpdateDisplayName(string updatedName)
    {
        if (string.IsNullOrWhiteSpace(updatedName))
        {
            return;
        }

        lock (gate)
        {
            displayName = updatedName;
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (started || disposed)
            {
                return Task.CompletedTask;
            }

            started = true;
            connectivityManager = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        }

        RefreshSelectedNetworks();
        RegisterConnectivityCallback();
        RebindReceiveSockets();
        AcquireMulticastLockSafe();
        StartNsdSafe();
        return Task.CompletedTask;
    }

    public Task AnnounceAsync(ushort port, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string currentName;
        AndroidNetworkDescriptor[] targets;
        lock (gate)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            currentName = displayName;
        }

        RefreshSelectedNetworks();
        lock (gate)
        {
            targets = selectedNetworks.ToArray();
        }

        var encoded = BeaconCodec.Encode(digest, port, currentName);
        if (encoded.IsFailure || encoded.Value is null)
        {
            return Task.CompletedTask;
        }

        var payload = encoded.Value;
        foreach (var net in targets)
        {
            SendBeaconOnNetwork(net, payload, beaconPort);
        }

        return Task.CompletedTask;
    }

    public void Rebuild(IReadOnlyList<OnLinkPrefix> prefixes)
    {
        _ = prefixes;
        RebindReceiveSockets();
    }

    internal int SendBeaconOnceForTest(
        int targetBeaconPort,
        Action<AndroidNetworkDescriptor, IPEndPoint, byte[]>? observer = null)
    {
        string currentName;
        lock (gate)
        {
            currentName = displayName;
        }

        RefreshSelectedNetworks();
        AndroidNetworkDescriptor[] targets;
        lock (gate)
        {
            targets = selectedNetworks.ToArray();
        }

        var encoded = BeaconCodec.Encode(digest, quicPort, currentName);
        if (encoded.IsFailure || encoded.Value is null)
        {
            return 0;
        }

        var sentCount = 0;
        foreach (var net in targets)
        {
            sentCount += SendBeaconOnNetwork(net, encoded.Value, targetBeaconPort, observer);
        }

        return sentCount;
    }

    internal void InjectNsdRecord(string foundDeviceId, string foundName, string hostAddress, int port)
    {
        if (string.IsNullOrWhiteSpace(foundDeviceId) || foundDeviceId == deviceId)
        {
            return;
        }

        byte[] peerDigest;
        try
        {
            peerDigest = Base32.DigestFromDeviceId(foundDeviceId);
        }
        catch (FormatException)
        {
            return;
        }

        if (peerDigest.AsSpan().SequenceEqual(digest) || !System.Net.IPAddress.TryParse(hostAddress, out _))
        {
            return;
        }

        PeerDiscovered?.Invoke(new DiscoveredPeer(foundDeviceId, peerDigest, hostAddress, port, foundName));
    }

    void RefreshSelectedNetworks()
    {
        IReadOnlyList<AndroidNetworkDescriptor>? custom;
        ConnectivityManager? cm;
        lock (gate)
        {
            custom = overrideNetworks;
            cm = connectivityManager ?? (context.GetSystemService(Context.ConnectivityService) as ConnectivityManager);
            connectivityManager = cm;
        }

        if (custom is not null)
        {
            var filteredCustom = FilterEligibleNetworks(custom).ToList();
            lock (gate)
            {
                selectedNetworks = filteredCustom;
            }

            return;
        }

        var discovered = new List<AndroidNetworkDescriptor>();
        if (cm is not null)
        {
            try
            {
#pragma warning disable CA1422
                var all = cm.GetAllNetworks();
#pragma warning restore CA1422
                if (all is not null)
                {
                    foreach (var network in all)
                    {
                        if (network is null)
                        {
                            continue;
                        }

                        var caps = cm.GetNetworkCapabilities(network);
                        if (caps is null)
                        {
                            continue;
                        }

                        var hasWifi = caps.HasTransport(TransportType.Wifi);
                        var hasEthernet = caps.HasTransport(TransportType.Ethernet);
                        var hasCellular = caps.HasTransport(TransportType.Cellular);
                        var hasVpn = caps.HasTransport(TransportType.Vpn);
                        if (!IsEligibleNetwork(hasWifi, hasEthernet, hasCellular, hasVpn))
                        {
                            continue;
                        }

                        var linkProps = cm.GetLinkProperties(network);
                        var prefixes = ExtractPrefixes(linkProps);
                        if (prefixes.Count == 0)
                        {
                            continue;
                        }

                        discovered.Add(new AndroidNetworkDescriptor(
                            network.NetworkHandle.ToString(),
                            hasWifi,
                            hasEthernet,
                            hasCellular,
                            hasVpn,
                            prefixes,
                            network));
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("connectivity-query-failed " + ex.Message);
            }
        }

        lock (gate)
        {
            selectedNetworks = discovered;
        }
    }

    static List<OnLinkPrefix> ExtractPrefixes(LinkProperties? props)
    {
        var list = new List<OnLinkPrefix>();
        if (props?.LinkAddresses is null)
        {
            return list;
        }

        var ifIndex = 1;
        if (!string.IsNullOrEmpty(props.InterfaceName))
        {
            try
            {
                var javaIf = NetworkInterface.GetByName(props.InterfaceName);
                if (javaIf is { Index: > 0 })
                {
                    ifIndex = javaIf.Index;
                }
            }
            catch
            {
            }
        }

        foreach (var linkAddr in props.LinkAddresses)
        {
            var rawBytes = linkAddr?.Address?.GetAddress();
            if (rawBytes is null || (rawBytes.Length != 4 && rawBytes.Length != 16))
            {
                continue;
            }

            var ip = new System.Net.IPAddress(rawBytes);
            if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6Multicast)
            {
                continue;
            }

            var isLinkLocal = ip.IsIPv6LinkLocal || IsIpv4LinkLocal(rawBytes);
            var text = rawBytes.Length == 16 && isLinkLocal && ifIndex > 0
                ? $"{ip}%{ifIndex}"
                : ip.ToString();
            list.Add(new OnLinkPrefix(text, linkAddr?.PrefixLength ?? (rawBytes.Length == 4 ? 24 : 64))
            {
                InterfaceIndex = ifIndex,
                LinkLocal = isLinkLocal,
            });
        }

        return list;
    }

    static bool IsIpv4LinkLocal(byte[] bytes) =>
        bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;

    void RegisterConnectivityCallback()
    {
        ConnectivityManager? cm;
        lock (gate)
        {
            cm = connectivityManager;
            if (cm is null || networkCallback is not null)
            {
                return;
            }
        }

        try
        {
            var request = new NetworkRequest.Builder()
                .AddTransportType(TransportType.Wifi)?
                .AddTransportType(TransportType.Ethernet)?
                .RemoveCapability(NetCapability.NotRestricted)?
                .Build();
            if (request is null)
            {
                return;
            }

            var callback = new LanNetworkCallback(OnPlatformNetworkEvent);
            cm.RegisterNetworkCallback(request, callback);
            lock (gate)
            {
                networkCallback = callback;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("network-callback-skipped " + ex.Message);
        }
    }

    void OnPlatformNetworkEvent()
    {
        RefreshSelectedNetworks();
        RebindReceiveSockets();
        NetworkChanged?.Invoke();
    }

    void RebindReceiveSockets()
    {
        AndroidNetworkDescriptor[] current;
        lock (gate)
        {
            if (disposed || !started)
            {
                return;
            }

            foreach (var old in receiveSockets)
            {
                try
                {
                    old.Close();
                    old.Dispose();
                }
                catch
                {
                }
            }

            receiveSockets.Clear();
            current = selectedNetworks.ToArray();
        }

        if (current.Length == 0)
        {
            return;
        }

        // Bind a receive DatagramSocket per eligible platform network (or one loopback-capable socket when testing with synthetic descriptors).
        var boundAny = false;
        foreach (var net in current)
        {
            if (net.PlatformNetwork is null)
            {
                continue;
            }

            var sock = TryOpenBoundReceiveSocket(net.PlatformNetwork, beaconPort);
            if (sock is not null)
            {
                boundAny = true;
                lock (gate)
                {
                    receiveSockets.Add(sock);
                }

                _ = Task.Run(() => ReceiveBeaconLoop(sock, stop.Token), stop.Token);
            }
        }

        if (!boundAny && current.Any(n => n.PlatformNetwork is null))
        {
            var fallbackTestSock = TryOpenBoundReceiveSocket(null, beaconPort);
            if (fallbackTestSock is not null)
            {
                lock (gate)
                {
                    receiveSockets.Add(fallbackTestSock);
                }

                _ = Task.Run(() => ReceiveBeaconLoop(fallbackTestSock, stop.Token), stop.Token);
            }
        }
    }

    static DatagramSocket? TryOpenBoundReceiveSocket(Network? platformNetwork, int port)
    {
        try
        {
            var socket = new DatagramSocket((Java.Net.SocketAddress?)null);
            socket.ReuseAddress = true;
            socket.Broadcast = true;
            platformNetwork?.BindSocket(socket);
            socket.Bind(new InetSocketAddress(port));
            return socket;
        }
        catch
        {
            return null;
        }
    }

    void ReceiveBeaconLoop(DatagramSocket socket, CancellationToken ct)
    {
        var buffer = new byte[2048];
        try
        {
            while (!ct.IsCancellationRequested && !socket.IsClosed)
            {
                using var packet = new DatagramPacket(buffer, buffer.Length);
                socket.Receive(packet);
                var length = packet.Length;
                var offset = packet.Offset;
                var rawData = packet.GetData();
                if (length <= 0 || rawData is null || offset < 0 || offset + length > rawData.Length)
                {
                    continue;
                }

                var decoded = BeaconCodec.Decode(rawData.AsSpan(offset, length));
                if (decoded.IsFailure || decoded.Value is null)
                {
                    continue;
                }

                var beacon = decoded.Value;
                if (beacon.Digest.AsSpan().SequenceEqual(digest))
                {
                    continue;
                }

                var addrBytes = packet.Address?.GetAddress();
                if (addrBytes is null)
                {
                    continue;
                }

                var senderIp = new System.Net.IPAddress(addrBytes);
                var normalized = senderIp.IsIPv4MappedToIPv6 ? senderIp.MapToIPv4().ToString() : senderIp.ToString();
                var peerId = "ca1" + Base32.Encode(beacon.Digest);
                if (peerId == deviceId)
                {
                    continue;
                }

                PeerDiscovered?.Invoke(new DiscoveredPeer(peerId, beacon.Digest, normalized, beacon.Port, beacon.Name));
            }
        }
        catch
        {
            // Socket closed on network change or dispose.
        }
    }

    int SendBeaconOnNetwork(
        AndroidNetworkDescriptor net,
        byte[] payload,
        int targetPort,
        Action<AndroidNetworkDescriptor, IPEndPoint, byte[]>? observer = null)
    {
        var sent = 0;
        var destinations = new List<System.Net.IPAddress>();
        foreach (var prefix in net.Prefixes)
        {
            if (System.Net.IPAddress.TryParse(prefix.Address, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (System.Net.IPAddress.IsLoopback(ip))
                {
                    destinations.Add(System.Net.IPAddress.Loopback);
                }
                else
                {
                    destinations.Add(SubnetBroadcast(ip, prefix.PrefixLength));
                }
            }
        }

        foreach (var targetIp in destinations.Distinct())
        {
            try
            {
                using var socket = new DatagramSocket();
                socket.Broadcast = true;
                net.PlatformNetwork?.BindSocket(socket);
                var rawTarget = targetIp.GetAddressBytes();
                using var inetAddr = InetAddress.GetByAddress(rawTarget);
                if (inetAddr is null)
                {
                    continue;
                }

                using var packet = new DatagramPacket(payload, payload.Length, inetAddr, targetPort);
                socket.Send(packet);
                observer?.Invoke(net, new IPEndPoint(targetIp, targetPort), payload);
                sent++;
            }
            catch (Exception ex)
            {
                log?.Invoke("beacon-send-failed " + net.Id + " " + ex.Message);
            }
        }

        return sent;
    }

    static System.Net.IPAddress SubnetBroadcast(System.Net.IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        var bits = prefixLength;
        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(bits, 0, 8);
            var mask = keep == 0 ? (byte)0 : (byte)(0xFF << (8 - keep));
            bytes[i] = (byte)(bytes[i] | (byte)~mask);
            bits -= keep;
        }

        return new System.Net.IPAddress(bytes);
    }

    void AcquireMulticastLockSafe()
    {
        try
        {
            var wifi = context.ApplicationContext?.GetSystemService(Context.WifiService) as WifiManager
                ?? context.GetSystemService(Context.WifiService) as WifiManager;
            if (wifi is null)
            {
                return;
            }

            var mlock = wifi.CreateMulticastLock("contextall-nsd");
            if (mlock is null)
            {
                return;
            }

            mlock.SetReferenceCounted(false);
            mlock.Acquire();
            lock (gate)
            {
                multicastLock = mlock;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("multicast-lock-degraded " + ex.Message);
        }
    }

    void StartNsdSafe()
    {
        if (forceNsdFailure)
        {
            lock (gate)
            {
                nsdDegraded = true;
                nsdRegistered = false;
            }

            log?.Invoke("nsd-degraded forced");
            return;
        }

        try
        {
            var manager = context.GetSystemService(Context.NsdService) as NsdManager;
            if (manager is null)
            {
                lock (gate)
                {
                    nsdDegraded = true;
                }

                log?.Invoke("nsd-degraded unavailable");
                return;
            }

            string currentName;
            lock (gate)
            {
                nsdManager = manager;
                currentName = displayName;
            }

            using var serviceInfo = new NsdServiceInfo
            {
                ServiceName = currentName,
                ServiceType = NsdServiceType,
                Port = quicPort,
            };
            serviceInfo.SetAttribute("id", deviceId);

            var regListener = new RegistrationHandler(
                onRegistered: () =>
                {
                    lock (gate)
                    {
                        nsdRegistered = true;
                    }
                },
                onFailed: code =>
                {
                    lock (gate)
                    {
                        nsdRegistered = false;
                        nsdDegraded = true;
                    }

                    log?.Invoke("nsd-register-failed " + code);
                });
            manager.RegisterService(serviceInfo, NsdProtocol.DnsSd, regListener);

            var discListener = new DiscoveryHandler(manager, OnNsdServiceResolved, code =>
            {
                lock (gate)
                {
                    nsdBrowsing = false;
                    nsdDegraded = true;
                }

                log?.Invoke("nsd-discover-failed " + code);
            });
            manager.DiscoverServices(NsdServiceType, NsdProtocol.DnsSd, discListener);

            lock (gate)
            {
                registrationListener = regListener;
                discoveryListener = discListener;
                nsdBrowsing = true;
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                nsdRegistered = false;
                nsdBrowsing = false;
                nsdDegraded = true;
            }

            log?.Invoke("nsd-degraded " + ex.GetType().Name + " " + ex.Message);
        }
    }

    void OnNsdServiceResolved(NsdServiceInfo info)
    {
        try
        {
            var attributes = info.Attributes;
            if (attributes is null || !attributes.TryGetValue("id", out var rawId) || rawId is null)
            {
                return;
            }

            var peerId = Encoding.UTF8.GetString(rawId).Trim();
            if (string.IsNullOrEmpty(peerId) || peerId == deviceId)
            {
                return;
            }

            string? hostIp = null;
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                var addresses = info.HostAddresses;
                if (addresses is not null)
                {
                    foreach (var inet in addresses)
                    {
                        var bytes = inet?.GetAddress();
                        if (bytes is { Length: 4 or 16 })
                        {
                            hostIp = new System.Net.IPAddress(bytes).ToString();
                            break;
                        }
                    }
                }
            }
            else
            {
#pragma warning disable CA1422
                var bytes = info.Host?.GetAddress();
#pragma warning restore CA1422
                if (bytes is { Length: 4 or 16 })
                {
                    hostIp = new System.Net.IPAddress(bytes).ToString();
                }
            }

            if (hostIp is null || info.Port <= 0)
            {
                return;
            }

            InjectNsdRecord(peerId, info.ServiceName ?? peerId, hostIp, info.Port);
        }
        catch (Exception ex)
        {
            log?.Invoke("nsd-resolve-parse-failed " + ex.Message);
        }
    }

    public void Dispose()
    {
        ConnectivityManager? cm;
        LanNetworkCallback? cb;
        NsdManager? nsd;
        RegistrationHandler? reg;
        DiscoveryHandler? disc;
        WifiManager.MulticastLock? mlock;
        DatagramSocket[] sockets;
        var wasBrowsing = false;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cm = connectivityManager;
            cb = networkCallback;
            networkCallback = null;
            nsd = nsdManager;
            reg = registrationListener;
            registrationListener = null;
            disc = discoveryListener;
            discoveryListener = null;
            wasBrowsing = nsdBrowsing;
            nsdBrowsing = false;
            nsdRegistered = false;
            mlock = multicastLock;
            multicastLock = null;
            sockets = receiveSockets.ToArray();
            receiveSockets.Clear();
        }

        stop.Cancel();
        stop.Dispose();

        foreach (var s in sockets)
        {
            try
            {
                s.Close();
                s.Dispose();
            }
            catch
            {
            }
        }

        if (cm is not null && cb is not null)
        {
            try
            {
                cm.UnregisterNetworkCallback(cb);
            }
            catch
            {
            }
        }

        if (nsd is not null)
        {
            if (wasBrowsing && disc is not null)
            {
                try
                {
                    nsd.StopServiceDiscovery(disc);
                }
                catch
                {
                }
            }

            if (reg is not null)
            {
                try
                {
                    nsd.UnregisterService(reg);
                }
                catch
                {
                }
            }
        }

        if (mlock is { IsHeld: true })
        {
            try
            {
                mlock.Release();
            }
            catch
            {
            }
        }

        mlock?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    sealed class LanNetworkCallback : ConnectivityManager.NetworkCallback
    {
        readonly Action? changed;

        public LanNetworkCallback(Action changed) => this.changed = changed;

        public LanNetworkCallback(IntPtr handle, global::Android.Runtime.JniHandleOwnership transfer)
            : base(handle, transfer)
        {
        }

        public override void OnAvailable(Network network) => changed?.Invoke();

        public override void OnLost(Network network) => changed?.Invoke();

        public override void OnCapabilitiesChanged(Network network, NetworkCapabilities networkCapabilities) => changed?.Invoke();

        public override void OnLinkPropertiesChanged(Network network, LinkProperties linkProperties) => changed?.Invoke();
    }

    sealed class RegistrationHandler : Java.Lang.Object, NsdManager.IRegistrationListener
    {
        readonly Action? onRegistered;
        readonly Action<NsdFailure>? onFailed;

        public RegistrationHandler(Action onRegistered, Action<NsdFailure> onFailed)
        {
            this.onRegistered = onRegistered;
            this.onFailed = onFailed;
        }

        public RegistrationHandler(IntPtr handle, global::Android.Runtime.JniHandleOwnership transfer)
            : base(handle, transfer)
        {
        }

        public void OnRegistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => onFailed?.Invoke(errorCode);

        public void OnServiceRegistered(NsdServiceInfo? serviceInfo) => onRegistered?.Invoke();

        public void OnServiceUnregistered(NsdServiceInfo? serviceInfo)
        {
        }

        public void OnUnregistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
        {
        }
    }

    sealed class DiscoveryHandler : Java.Lang.Object, NsdManager.IDiscoveryListener
    {
        readonly NsdManager? manager;
        readonly Action<NsdServiceInfo>? onResolved;
        readonly Action<NsdFailure>? onFailed;

        public DiscoveryHandler(
            NsdManager manager,
            Action<NsdServiceInfo> onResolved,
            Action<NsdFailure> onFailed)
        {
            this.manager = manager;
            this.onResolved = onResolved;
            this.onFailed = onFailed;
        }

        public DiscoveryHandler(IntPtr handle, global::Android.Runtime.JniHandleOwnership transfer)
            : base(handle, transfer)
        {
        }

        public void OnDiscoveryStarted(string? regType)
        {
        }

        public void OnDiscoveryStopped(string? serviceType)
        {
        }

        public void OnServiceFound(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is null || manager is null || onResolved is null)
            {
                return;
            }

            try
            {
#pragma warning disable CA1422
                manager.ResolveService(serviceInfo, new ResolveHandler(onResolved));
#pragma warning restore CA1422
            }
            catch
            {
            }
        }

        public void OnServiceLost(NsdServiceInfo? serviceInfo)
        {
        }

        public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode) => onFailed?.Invoke(errorCode);

        public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode)
        {
        }
    }

    sealed class ResolveHandler : Java.Lang.Object, NsdManager.IResolveListener
    {
        readonly Action<NsdServiceInfo>? onResolved;

        public ResolveHandler(Action<NsdServiceInfo> onResolved) => this.onResolved = onResolved;

        public ResolveHandler(IntPtr handle, global::Android.Runtime.JniHandleOwnership transfer)
            : base(handle, transfer)
        {
        }

        public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
        {
        }

        public void OnServiceResolved(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is not null)
            {
                onResolved?.Invoke(serviceInfo);
            }
        }
    }
}
