using ContextAll.Core;

namespace ContextAll.Session.Abstractions;

public sealed record DiscoveredPeer
{
    public string DeviceId { get; init; } = "";
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public string? DisplayName { get; init; }
    public byte[]? Digest { get; init; }

    public DiscoveredPeer() { }

    public DiscoveredPeer(string deviceId, string address, int port, string? displayName = null, byte[]? digest = null)
    {
        DeviceId = deviceId;
        Address = address;
        Port = port;
        DisplayName = displayName;
        Digest = digest;
    }

    public DiscoveredPeer(string deviceId, byte[] digest, string address, int port, string? displayName)
    {
        DeviceId = deviceId;
        Digest = digest;
        Address = address;
        Port = port;
        DisplayName = displayName;
    }
}

public interface IDiscovery : IAsyncDisposable, IDisposable
{
    bool IsActive => true;
    bool OwnsNetworkAndSockets => false;
    IReadOnlyList<OnLinkPrefix>? GetActivePrefixes() => null;
    event Action? NetworkChanged
    {
        add { }
        remove { }
    }
    Task StartAsync(CancellationToken ct = default);
    Task AnnounceAsync(ushort port, CancellationToken ct = default);
    void UpdateDisplayName(string displayName) { }
    void Rebuild(IReadOnlyList<OnLinkPrefix> prefixes) { }
    event Action<DiscoveredPeer>? PeerDiscovered;
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
    void IDisposable.Dispose() { }
}
