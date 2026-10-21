namespace ContextAll.Core;

public sealed record OnLinkPrefix(string Address, int PrefixLength);

public sealed record RoutePath(string Address, int Port, bool FromDiscovery)
{
    public long RefreshedAt { get; set; }

    public long? FailedAt { get; set; }

    public RoutePath(string address, int port, long refreshedAt, bool fromDiscovery)
        : this(address, port, fromDiscovery) => RefreshedAt = refreshedAt;
}

public sealed class RouteTable
{
    readonly Dictionary<string, PeerRoutes> peers = [];
    readonly string localDigestHex;

    public RouteTable(byte[] localSpki)
    {
        localDigestHex = Convert.ToHexString(DeviceIds.Digest(localSpki));
    }

    public bool IsTrusted(string deviceId) => peers.TryGetValue(deviceId, out var peer) && peer.Trusted;

    public void RememberTrust(string deviceId)
    {
        Peer(deviceId).Trusted = true;
    }

    public void AddDiscovery(string deviceId, byte[] digest, string address, int port, long nowMs, string? displayName)
    {
        if (Convert.ToHexString(digest) == localDigestHex)
        {
            return;
        }

        var peer = Peer(deviceId);
        if (displayName is not null && DisplayNames.IsLegal(displayName))
        {
            peer.DisplayName = displayName;
        }

        Upsert(peer.Discovery, address, port, nowMs, fromDiscovery: true);
    }

    public void NoteSession(string deviceId, string address)
    {
        Peer(deviceId).SessionAddress = address;
        Peer(deviceId).Live = true;
    }

    public void CloseSession(string deviceId)
    {
        if (!peers.TryGetValue(deviceId, out var peer) || peer.SessionAddress is null)
        {
            return;
        }

        peer.Live = false;
        peer.Suppressed.Add(peer.SessionAddress);
        peer.SessionAddress = null;
    }

    public void Announce(string deviceId, IReadOnlyList<string> addresses, IReadOnlyList<OnLinkPrefix> local, long nowMs)
    {
        var peer = Peer(deviceId);
        peer.Announced.Clear();
        var kept = 0;
        foreach (var address in addresses)
        {
            if (kept == 8 || IsLinkLocal(address) || !IsOnLink(address, local))
            {
                continue;
            }

            peer.Announced.Add(new RoutePath(address, 42461, nowMs, false));
            kept++;
        }
    }

    public void Expire(long nowMs)
    {
        foreach (var peer in peers.Values)
        {
            peer.Discovery.RemoveAll(path => nowMs - path.RefreshedAt >= 180_000);
        }
    }

    public void FailDial(string deviceId, string address)
    {
        var path = Find(deviceId, address);
        if (path is not null)
        {
            path.FailedAt = path.RefreshedAt;
        }
    }

    public bool ShouldDial(string deviceId, bool startPairing)
    {
        if (!peers.TryGetValue(deviceId, out var peer))
        {
            return false;
        }

        if (!peer.Trusted && !startPairing)
        {
            return false;
        }

        if (peer.Live)
        {
            return false;
        }

        var candidate = DialCandidates(deviceId).FirstOrDefault();
        if (candidate is null)
        {
            return false;
        }

        return candidate.FailedAt != candidate.RefreshedAt;
    }

    public IReadOnlyList<RoutePath> DialCandidates(string deviceId)
    {
        if (!peers.TryGetValue(deviceId, out var peer))
        {
            return [];
        }

        return peer.Discovery
            .Concat(peer.Announced)
            .Where(path => !peer.Suppressed.Contains(path.Address))
            .OrderByDescending(path => path.RefreshedAt)
            .ToArray();
    }

    public IReadOnlyList<RoutePath> DiscoveryPaths(string deviceId) =>
        peers.TryGetValue(deviceId, out var peer) ? peer.Discovery : [];

    public string? DisplayNameOf(string deviceId) =>
        peers.TryGetValue(deviceId, out var peer) ? peer.DisplayName : null;

    static void Upsert(List<RoutePath> paths, string address, int port, long nowMs, bool fromDiscovery)
    {
        var existing = paths.FirstOrDefault(path => path.Address == address && path.Port == port);
        if (existing is null)
        {
            paths.Add(new RoutePath(address, port, nowMs, fromDiscovery));
            return;
        }

        existing.RefreshedAt = nowMs;
    }

    RoutePath? Find(string deviceId, string address)
    {
        if (!peers.TryGetValue(deviceId, out var peer))
        {
            return null;
        }

        return peer.Discovery.Concat(peer.Announced).FirstOrDefault(path => path.Address == address);
    }

    PeerRoutes Peer(string deviceId)
    {
        if (!peers.TryGetValue(deviceId, out var peer))
        {
            peer = new PeerRoutes();
            peers[deviceId] = peer;
        }

        return peer;
    }

    static bool IsLinkLocal(string address)
    {
        if (!System.Net.IPAddress.TryParse(address, out var ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }

        var raw = ip.GetAddressBytes();
        return raw.Length == 16 && raw[0] == 0xfe && (raw[1] & 0xc0) == 0x80;
    }

    static bool IsOnLink(string address, IReadOnlyList<OnLinkPrefix> local)
    {
        if (!System.Net.IPAddress.TryParse(address, out var ip))
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        foreach (var prefix in local)
        {
            if (!System.Net.IPAddress.TryParse(prefix.Address, out var network))
            {
                continue;
            }

            var networkBytes = network.GetAddressBytes();
            if (networkBytes.Length != bytes.Length)
            {
                continue;
            }

            var bits = prefix.PrefixLength;
            var matched = true;
            for (var i = 0; i < bytes.Length && bits > 0; i++)
            {
                var take = Math.Min(8, bits);
                var mask = (byte)(0xFF << (8 - take));
                if ((bytes[i] & mask) != (networkBytes[i] & mask))
                {
                    matched = false;
                    break;
                }

                bits -= take;
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    sealed class PeerRoutes
    {
        public bool Trusted { get; set; }

        public bool Live { get; set; }

        public string? SessionAddress { get; set; }

        public string? DisplayName { get; set; }

        public List<RoutePath> Discovery { get; } = [];

        public List<RoutePath> Announced { get; } = [];

        public HashSet<string> Suppressed { get; } = [];
    }
}
