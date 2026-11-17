using System.Net;
using System.Net.NetworkInformation;
using System.Net.Quic;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session.Abstractions;

namespace ContextAll.Session;

public sealed class SessionHost : IAsyncDisposable, IDisposable
{
    public const int BeaconPort = 42462;

    internal const int BeaconIntervalMs = 5000;

    internal const int ReconcileSeconds = 5;

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

            socket.Bind(new IPEndPoint(any, BeaconPort));
            return socket;
        }
        catch (SocketException)
        {
            Console.Error.WriteLine("beacon-skipped " + family);
            return null;
        }
    }

    readonly IdentityMaterial identity;
    readonly string settingsPath;
    readonly string home;
    readonly SettingsDocument settings;
    readonly Dictionary<string, TrustDocument> trust = [];
    readonly Dictionary<string, byte[]> files = [];
    readonly Dictionary<string, bool> remoteClipboard = [];
    readonly Dictionary<string, HybridClock> remoteClocks = [];
    readonly Dictionary<string, string> announcedPresence = [];
    readonly System.Collections.Generic.HashSet<string> seen = [];
    readonly System.Collections.Generic.HashSet<string> pairingWanted = [];
    readonly System.Collections.Generic.HashSet<string> dialing = [];
    readonly List<PeerLink> links = [];
    readonly List<OnLinkPrefix> prefixes = [];
    readonly CancellationTokenSource stop = new();
    readonly SemaphoreSlim rebuildGate = new(1, 1);
    CancellationTokenSource? run;
    readonly RouteTable routes;
    IReadOnlyList<OnLinkPrefix> lastNetwork = [];
    long lastBeaconTick;
    ListenerSet? listenerSet;
    NetworkMonitor? networkMonitor;
    Socket? udp;
    Socket? udp6;
    MdnsDiscovery? mdns;
    readonly IClipboardSource? clipboard;
    readonly IDiscovery? discovery;
    readonly ITransferSink? transferSink;
    readonly IFirewallProfile? firewall;
    readonly int quicPort;
    PeerLink? prompt;
    string? pendingText;
    bool pendingWrite;
    bool disposed;

    public event Action<IReadOnlyList<Frame>>? Published;

    public SessionHost(
        IHostPaths paths,
        IClipboardSource? clipboard = null,
        IDiscovery? discovery = null,
        ITransferSink? transferSink = null,
        int quicPort = QuicLink.Port,
        IdentityMaterial? preloadedIdentity = null,
        IFirewallProfile? firewall = null)
    {
        this.clipboard = clipboard;
        this.discovery = discovery;
        this.transferSink = transferSink;
        this.firewall = firewall;
        this.quicPort = quicPort;

        if (preloadedIdentity is not null)
        {
            this.identity = preloadedIdentity;
        }
        else
        {
            var idStart = IdentityStore.Start(paths.IdentityDirectory);
            if (!idStart.Ok || idStart.Material is null)
            {
                throw new InvalidOperationException("identity");
            }
            this.identity = idStart.Material;
        }
        this.settingsPath = paths.SettingsFile;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Path.GetTempPath();
        this.home = home;

        var settingsStart = SettingsStore.Start(this.settingsPath, DisplayNames.Initial(Environment.MachineName));
        if (!settingsStart.Ok || settingsStart.Document is null)
        {
            throw new InvalidOperationException("settings");
        }
        this.settings = settingsStart.Document;

        LocalSpki = identity.Spki;
        DisplayName = string.IsNullOrEmpty(settings.DisplayName)
            ? DisplayNames.Initial(Environment.MachineName)
            : settings.DisplayName;
        routes = new RouteTable(identity.Spki);
        foreach (var entry in settings.Trust)
        {
            trust[entry.DeviceId] = entry;
            routes.RememberTrust(entry.DeviceId);
        }

        Clipboard = new ClipboardNode(identity.DeviceId);
        var clipboardPath = Path.Combine(paths.StateDirectory, "clipboard.json");
        ClipboardFiles.Listen(clipboardPath, null, false);
        ClipboardPath = clipboardPath;
        try
        {
            var document = ClipboardFiles.Read(clipboardPath);
            Clipboard.Restore(document);
            Applied = document.AppliedText;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            Applied = "";
        }

        Store = new TransferStore(paths.TransfersDirectory);
        NotifyLog = Path.Combine(paths.StateDirectory, "notifications.log");
        Downloads = string.IsNullOrEmpty(settings.DownloadDirectory)
            ? paths.DefaultDownloadDirectory
            : settings.DownloadDirectory;
        Directory.CreateDirectory(Downloads);
    }

    internal SessionHost(
        IdentityMaterial identity,
        string settingsPath,
        string home,
        SettingsDocument settings,
        string stateDirectory,
        IClipboardSource? clipboard = null,
        IDiscovery? discovery = null,
        ITransferSink? transferSink = null,
        int quicPort = QuicLink.Port,
        IFirewallProfile? firewall = null)
    {
        this.identity = identity;
        this.settingsPath = settingsPath;
        this.home = home;
        this.settings = settings;
        this.clipboard = clipboard;
        this.discovery = discovery;
        this.transferSink = transferSink;
        this.firewall = firewall;
        this.quicPort = quicPort;
        LocalSpki = identity.Spki;
        DisplayName = string.IsNullOrEmpty(settings.DisplayName)
            ? DisplayNames.Initial(Environment.MachineName)
            : settings.DisplayName;
        routes = new RouteTable(identity.Spki);
        foreach (var entry in settings.Trust)
        {
            trust[entry.DeviceId] = entry;
            routes.RememberTrust(entry.DeviceId);
        }

        Clipboard = new ClipboardNode(identity.DeviceId);
        var clipboardPath = Path.Combine(stateDirectory, "clipboard.json");
        ClipboardFiles.Listen(clipboardPath, null, false);
        ClipboardPath = clipboardPath;
        try
        {
            var document = ClipboardFiles.Read(clipboardPath);
            Clipboard.Restore(document);
            Applied = document.AppliedText;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            Applied = "";
        }

        Store = new TransferStore(Path.Combine(stateDirectory, "transfers"));
        NotifyLog = Path.Combine(stateDirectory, "notifications.log");
        Downloads = SettingsStore.ResolveDownload(settings.DownloadDirectory, home);
        Directory.CreateDirectory(Downloads);
    }

    public object Gate { get; } = new();

    public string DisplayName { get; private set; }

    public string LocalDeviceId => identity.DeviceId;

    public int QuicPort => quicPort;

    public string ClipboardBackend => clipboard?.BackendName ?? "unavailable";

    public int ActiveListenerCount => listenerSet?.Count ?? 0;

    public byte[] LocalSpki { get; }

    public ClipboardNode Clipboard { get; }

    public TransferStore Store { get; }

    public string Downloads { get; private set; }

    public string NotifyLog { get; }

    public string Applied { get; set; }

    public string ClipboardPath { get; }

    public static SessionHost CreateIsolated(string root)
    {
        var identity = IdentityStore.Start(Path.Combine(root, "identity"));
        if (!identity.Ok || identity.Material is null)
        {
            throw new InvalidOperationException("identity");
        }

        var settings = SettingsStore.Start(Path.Combine(root, "config", "settings.yaml"), "ContextAll");
        if (!settings.Ok || settings.Document is null)
        {
            throw new InvalidOperationException("settings");
        }

        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(home);
        return new SessionHost(identity.Material, Path.Combine(root, "config", "settings.yaml"), home, settings.Document, Path.Combine(root, "state"));
    }

    public void Track(PeerLink link)
    {
        lock (Gate)
        {
            links.Add(link);
        }
    }

    public void Start(CancellationToken cancellationToken)
    {
        run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
        var token = run.Token;
        if (clipboard is not null)
        {
            Console.Error.WriteLine(clipboard.IsSupported ? "clipboard-ready" : "clipboard-disabled");
            if (clipboard.IsSupported)
            {
                clipboard.PlainText += OnPlain;
                clipboard.ClipboardChanged += (mime, data) =>
                {
                    if (mime == "text/plain;charset=utf-8" || mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
                    {
                        OnPlain(Encoding.UTF8.GetString(data));
                    }
                };
            }
        }
        else
        {
            Console.Error.WriteLine("clipboard-disabled");
        }

        var customDiscovery = discovery is not null;
        var ownsNetwork = discovery is { OwnsNetworkAndSockets: true };
        if (discovery is not null)
        {
            discovery.UpdateDisplayName(DisplayName);
            discovery.PeerDiscovered += OnCustomPeerDiscovered;
            if (ownsNetwork)
            {
                discovery.NetworkChanged += OnCustomNetworkChanged;
            }

            _ = StartCustomDiscoveryAsync(discovery, ownsNetwork, token);
        }

        _ = Task.Run(() => ListenAsync(token), token);

        if (!customDiscovery)
        {
            udp = ListenBeacon(AddressFamily.InterNetwork, IPAddress.Any);
            udp6 = ListenBeacon(AddressFamily.InterNetworkV6, IPAddress.IPv6Any);
        }

        List<OnLinkPrefix> localPrefixes;
        lock (Gate)
        {
            RefreshPrefixes();
            localPrefixes = prefixes.ToList();
        }

        if (!customDiscovery)
        {
            mdns = new MdnsDiscovery(DeviceIds.Text(identity.Spki), DisplayName, quicPort, NoteMdns);
            mdns.Start(localPrefixes);
        }

        lastNetwork = localPrefixes.ToArray();
        networkMonitor = new NetworkMonitor(() => _ = SafeRebuildAsync());
        if (firewall is not null)
        {
            firewall.Changed += OnFirewallChanged;
        }
        if (!ownsNetwork)
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }

        _ = Task.Run(() => ReconcileLoop(token), token);

        if (discovery is { } injectedDiscovery)
        {
            _ = Task.Run(() => CustomBeaconLoop(injectedDiscovery, token), token);
        }
        else
        {
            _ = Task.Run(() => BeaconLoop(token), token);
        }

        _ = Task.Run(() => DialLoop(token), token);
    }

    public Frame Snapshot()
    {
        lock (Gate)
        {
            return SnapshotFrame();
        }
    }

    public bool IsTrusted(string deviceId)
    {
        lock (Gate)
        {
            return trust.ContainsKey(deviceId);
        }
    }

    public bool Mutual(string deviceId)
    {
        lock (Gate)
        {
            return LocalClipboard(deviceId) && RemoteClipboard(deviceId);
        }
    }

    public bool LocalClipboard(string deviceId)
    {
        lock (Gate)
        {
            return trust.TryGetValue(deviceId, out var entry) && entry.ClipboardSync;
        }
    }

    public bool RemoteClipboard(string deviceId)
    {
        lock (Gate)
        {
            return remoteClipboard.TryGetValue(deviceId, out var enabled) && enabled;
        }
    }

    public bool Ask(string deviceId)
    {
        lock (Gate)
        {
            return !trust.TryGetValue(deviceId, out var entry) || entry.AskBeforeSave;
        }
    }

    public HybridClock? RemoteClock(string deviceId)
    {
        lock (Gate)
        {
            return remoteClocks.TryGetValue(deviceId, out var clock) ? clock : null;
        }
    }

    public void NoteRemoteClipboard(string deviceId, bool enabled)
    {
        lock (Gate)
        {
            remoteClipboard[deviceId] = enabled;
            Clipboard.Connect(deviceId, LocalClipboard(deviceId), enabled);
            if (LocalClipboard(deviceId) && enabled)
            {
                Console.Error.WriteLine("clipboard-mutual " + deviceId);
            }

            Publish([DeviceEvent(deviceId, Presence(deviceId))]);
        }
    }

    public void RememberRemoteClock(string deviceId, HybridClock clock)
    {
        lock (Gate)
        {
            remoteClocks[deviceId] = clock;
        }
    }

    public void RememberBytes(string transferId, byte[] data)
    {
        lock (Gate)
        {
            files[transferId] = data;
        }
    }

    public bool TryBytes(string transferId, out byte[] data)
    {
        lock (Gate)
        {
            return files.TryGetValue(transferId, out data!);
        }
    }

    public void ForgetBytes(string transferId)
    {
        lock (Gate)
        {
            files.Remove(transferId);
        }
    }

    public void SaveTrust(TrustEntry entry)
    {
        lock (Gate)
        {
            if (trust.TryGetValue(entry.DeviceId, out var existing))
            {
                if (!string.IsNullOrEmpty(entry.DisplayName))
                {
                    existing.DisplayName = entry.DisplayName;
                }

                if (!string.IsNullOrEmpty(entry.SpkiBase64))
                {
                    existing.Spki = entry.SpkiBase64;
                }
            }
            else
            {
                var created = new TrustDocument
                {
                    DeviceId = entry.DeviceId,
                    DisplayName = entry.DisplayName,
                    Spki = entry.SpkiBase64,
                    ClipboardSync = false,
                    AskBeforeSave = true,
                };
                trust[entry.DeviceId] = created;
                settings.Trust.Add(created);
            }

            routes.RememberTrust(entry.DeviceId);
            Persist();
        }
    }

    public void RemoveTrust(string deviceId, bool notify = true)
    {
        PeerLink? live = null;
        lock (Gate)
        {
            trust.Remove(deviceId);
            settings.Trust.RemoveAll(item => item.DeviceId == deviceId);
            routes.ForgetTrust(deviceId);
            remoteClipboard.Remove(deviceId);
            Persist();
            live = Live(deviceId);
        }

        if (notify)
        {
            live?.SendUnpair();
        }
        else
        {
            live?.AbandonTransfers();
        }

        Store.DeletePeer(deviceId);
    }

    public void RenamePeer(string deviceId, string name)
    {
        lock (Gate)
        {
            if (trust.TryGetValue(deviceId, out var entry))
            {
                entry.DisplayName = name;
                Persist();
            }
        }
    }

    public void NoteAnnouncement(string deviceId, string[] addresses)
    {
        lock (Gate)
        {
            RefreshPrefixes();
            routes.Announce(deviceId, addresses, prefixes, Environment.TickCount64);
        }
    }

    public string[] LocalAddresses()
    {
        lock (Gate)
        {
            RefreshPrefixes();
            return prefixes.Select(prefix => prefix.Address).Distinct().ToArray();
        }
    }

    public void Settle(PeerLink link)
    {
        lock (Gate)
        {
            var peers = links.Where(item => item.RemoteId == link.RemoteId && !item.Closed).ToList();
            if (peers.Count < 2)
            {
                link.Adopt();
                return;
            }

            link.ResolveDuplicate(peers.First(item => !ReferenceEquals(item, link)));
        }
    }

    public bool OtherLive(PeerLink link)
    {
        lock (Gate)
        {
            return links.Any(item => !ReferenceEquals(item, link) && item.RemoteId == link.RemoteId && !item.Closed);
        }
    }

    public void ShowPrompt(PeerLink link)
    {
        lock (Gate)
        {
            prompt = link;
            Publish([PromptFrame(link)]);
        }
    }

    public void EndPairing(PeerLink link, string outcome)
    {
        lock (Gate)
        {
            if (ReferenceEquals(prompt, link))
            {
                prompt = null;
            }

            pairingWanted.Remove(link.RemoteId);
            Publish([Ended(link.RemoteId, outcome)]);
        }
    }

    public void OnEntered(PeerLink link)
    {
        lock (Gate)
        {
            if (ReferenceEquals(prompt, link))
            {
                prompt = null;
            }

            pairingWanted.Remove(link.RemoteId);
        }
    }

    public void LinkClosed(PeerLink link)
    {
        lock (Gate)
        {
            if (!links.Remove(link))
            {
                if (!links.Any(item => item.RemoteId == link.RemoteId && !item.Closed))
                {
                    routes.CloseSession(link.RemoteId);
                }

                return;
            }

            if (!links.Any(item => item.RemoteId == link.RemoteId && !item.Closed))
            {
                routes.CloseSession(link.RemoteId);
            }
            if (ReferenceEquals(prompt, link))
            {
                prompt = null;
                Publish([Ended(link.RemoteId, "closed")]);
            }

            Publish([DeviceEvent(link.RemoteId, Presence(link.RemoteId))]);
        }
    }

    public void Publish(IReadOnlyList<Frame> frames)
    {
        Published?.Invoke(frames);
    }

    public Frame DeviceEvent(string deviceId, string presence)
    {
        lock (Gate)
        {
            return FrameMessages.Json(
                FrameTypes.DeviceChanged,
                2,
                0,
                FrameTypes.SessionStream,
                Describe(deviceId, presence),
                ProtocolJsonContext.Default.DeviceEventPayload);
        }
    }

    public Frame WithSyncFlag(Frame frame, bool enabled)
    {
        var vector = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.VectorPayload).Value;
        if (vector is null)
        {
            return frame;
        }

        return FrameMessages.Json(
            FrameTypes.CrdtStateVector,
            frame.Priority,
            frame.Flags,
            frame.StreamId,
            vector with { ClipboardSync = enabled },
            ProtocolJsonContext.Default.VectorPayload);
    }

    public void SaveClipboard(string applied)
    {
        lock (Gate)
        {
            try
            {
                ClipboardFiles.Write(ClipboardPath, Clipboard.ToDocument(applied));
            }
            catch (IOException)
            {
                // The next change tries again.
            }
        }
    }

    public void WriteClipboard(string text)
    {
        lock (Gate)
        {
            pendingText = text;
            pendingWrite = true;
        }
    }

    public void FlushClipboard()
    {
        string? text;
        lock (Gate)
        {
            if (!pendingWrite)
            {
                return;
            }

            pendingWrite = false;
            text = pendingText ?? "";
        }

        if (clipboard is not null && clipboard.IsSupported)
        {
            clipboard.SetText(text);
            _ = clipboard.SetClipboardAsync("text/plain;charset=utf-8", Encoding.UTF8.GetBytes(text), CancellationToken.None);
        }
    }

    public bool CommitInbound(TransferSlot slot)
    {
        if (transferSink is not { HandlesCommitInbound: true } sink)
        {
            return Store.TryPlace(slot, Downloads);
        }

        var part = Store.PartPath(slot.TransferId);
        Store.ClosePart(slot.TransferId);
        if (!File.Exists(part) || new FileInfo(part).Length != slot.TotalSize)
        {
            return false;
        }

        if (!sink.VerifyChecksum(part, slot.RootSha256))
        {
            return false;
        }

        if (!sink.TryCommitInbound(part, slot.FileName, slot.RootSha256, Downloads, out _))
        {
            return false;
        }

        sink.CleanupPartial(part);
        Store.Delete(slot.TransferId);
        return true;
    }

    public async Task<(bool Ok, string? DeviceId, string? Error)> ConnectEndpointAsync(
        IPEndPoint remote,
        string? alias = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);

        var sessionToken = run?.Token ?? stop.Token;
        if (disposed || sessionToken.IsCancellationRequested)
        {
            return (false, null, "not-running");
        }

        var addressText = LocalNetwork.Text(remote.Address);
        lock (Gate)
        {
            if (disposed)
            {
                return (false, null, "not-running");
            }

            if (!OnLinkUnlocked(addressText))
            {
                return (false, null, "off-link");
            }
        }

        using var dial = CancellationTokenSource.CreateLinkedTokenSource(sessionToken, cancellationToken);
        dial.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var (connection, peerCert, peerDer) = await ConnectMutualInternalAsync(remote, dial.Token).ConfigureAwait(false);
            var bytes = peerDer is not null
                ? CertificateChecks.LeafSpki(peerDer)
                : peerCert is not null ? CertificateChecks.LeafSpki(peerCert) : null;
            if (bytes is null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return (false, null, "bad-certificate");
            }

            var id = DeviceIds.Text(bytes);
            if (id == DeviceIds.Text(identity.Spki))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return (false, null, "self");
            }

            var digest = DeviceIds.Digest(bytes);
            lock (Gate)
            {
                if (disposed)
                {
                    _ = connection.DisposeAsync();
                    return (false, null, "not-running");
                }

                seen.Add(id);
                routes.AddDiscovery(id, digest, addressText, remote.Port, Environment.TickCount64, alias);
                if (!trust.ContainsKey(id))
                {
                    pairingWanted.Add(id);
                }
            }

            _ = Attach(connection, peerCert, initiated: true, sessionToken, dialedId: null, dialedAddress: addressText, peerDer: peerDer);
            return (true, id, null);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or QuicException or InvalidOperationException or SocketException or AuthenticationException)
        {
            return (false, null, "connect-failed");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stop.Cancel();
        run?.Cancel();
        if (discovery is not null)
        {
            discovery.PeerDiscovered -= OnCustomPeerDiscovered;
            discovery.NetworkChanged -= OnCustomNetworkChanged;
        }

        if (firewall is not null)
        {
            firewall.Changed -= OnFirewallChanged;
            if (firewall is IDisposable disposableFirewall)
            {
                disposableFirewall.Dispose();
            }
        }

        clipboard?.Dispose();
        discovery?.Dispose();
        mdns?.Dispose();
        networkMonitor?.Dispose();
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        udp?.Dispose();
        udp6?.Dispose();
        if (listenerSet is not null)
        {
            _ = listenerSet.DisposeAsync().AsTask();
        }

        run?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (discovery is not null)
        {
            await discovery.DisposeAsync().ConfigureAwait(false);
        }
        if (clipboard is not null)
        {
            await clipboard.DisposeAsync().ConfigureAwait(false);
        }
    }

    async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!QuicLink.Supported)
            {
                Console.Error.WriteLine("quic-skipped quic-unsupported");
                PlatformCrypto.LogDiagnostic("quic-skipped quic-unsupported");
                return;
            }

            Func<IPAddress, CancellationToken, Task<IQuicListener>> listenFactory;
            if (QuicLink.Backend is IPortableQuicBackend portable && identity.Portable is { } credential)
            {
                if (identity.Spki.Length == 0)
                {
                    Console.Error.WriteLine("quic-skipped spki");
                    PlatformCrypto.LogDiagnostic("quic-skipped spki");
                    return;
                }

                listenFactory = (address, token) => portable.ListenMutualPortableAsync(address, credential, quicPort, token);
            }
            else
            {
                var certificate = CertificateFor(identity.Pem);
                var spki = CertificateChecks.LeafSpki(certificate);
                var matches = spki is not null && spki.AsSpan().SequenceEqual(identity.Spki);
                if (!matches)
                {
                    Console.Error.WriteLine("quic-skipped spki");
                    PlatformCrypto.LogDiagnostic("quic-skipped spki");
                    return;
                }

                listenFactory = (address, token) => QuicLink.Backend.ListenMutualAsync(address, certificate, quicPort, token);
            }

            listenerSet = new ListenerSet(
                listenFactory,
                (connection, token) => Attach(connection, null, initiated: false, token, null, null),
                message =>
                {
                    Console.Error.WriteLine(message);
                    PlatformCrypto.LogDiagnostic(message);
                },
                cancellationToken);
            await RebuildListenersAsync().ConfigureAwait(false);
            if (listenerSet.Count == 0)
            {
                var emptyMsg = QuicLink.Supported ? "quic-skipped no-beacon-address" : "quic-skipped quic-unsupported";
                Console.Error.WriteLine(emptyMsg);
                PlatformCrypto.LogDiagnostic(emptyMsg);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or QuicException or IOException or CryptographicException or AuthenticationException or OperationCanceledException)
        {
            var errMsg = "quic-skipped " + ex.GetType().Name + " " + ex.Message;
            Console.Error.WriteLine(errMsg);
            PlatformCrypto.LogDiagnostic(errMsg);
        }
    }

    async Task RebuildListenersAsync(IReadOnlyList<OnLinkPrefix>? local = null)
    {
        var set = listenerSet;
        if (set is null)
        {
            return;
        }

        if (local is null)
        {
            lock (Gate)
            {
                RefreshPrefixes();
                local = prefixes.ToArray();
            }
        }

        var addresses = new List<IPAddress>();
        foreach (var prefix in local)
        {
            if (IPAddress.TryParse(prefix.Address, out var address) && !addresses.Contains(address))
            {
                addresses.Add(address);
            }
        }

        await set.RebuildAsync(addresses).ConfigureAwait(false);
    }

    void OnNetworkAddressChanged(object? sender, EventArgs args) => networkMonitor?.Signal();

    void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => networkMonitor?.Signal();

    void OnFirewallChanged(object? sender, EventArgs args) => networkMonitor?.Signal();

    async Task ReconcileLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(ReconcileSeconds), cancellationToken).ConfigureAwait(false);
                await RebuildNetworkAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The daemon stopped.
        }
        catch (Exception ex) when (ex is IOException or SocketException or QuicException or InvalidOperationException)
        {
            Console.Error.WriteLine("network-reconcile " + ex.GetType().Name + " " + ex.Message);
        }
    }

    async Task SafeRebuildAsync()
    {
        try
        {
            await RebuildNetworkAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or QuicException or InvalidOperationException)
        {
            Console.Error.WriteLine("network-rebuild " + ex.GetType().Name + " " + ex.Message);
        }
    }

    async Task RebuildNetworkAsync(IReadOnlyList<OnLinkPrefix>? local = null, bool force = false)
    {
        if (disposed)
        {
            return;
        }

        await rebuildGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            bool changed;
            lock (Gate)
            {
                if (local is null)
                {
                    RefreshPrefixes();
                    local = prefixes.ToArray();
                }
                else if (firewall is not null)
                {
                    local = FirewallProfiles.Filter(local, firewall).ToArray();
                }

                changed = force || !SamePrefixes(lastNetwork, local);
                lastNetwork = local.ToArray();
            }

            if (changed)
            {
                mdns?.Rebuild(local);
                discovery?.Rebuild(local);
            }

            await RebuildListenersAsync(local).ConfigureAwait(false);
            if (!changed)
            {
                return;
            }

            if (discovery is not null)
            {
                Volatile.Write(ref lastBeaconTick, Environment.TickCount64);
                _ = discovery.AnnounceAsync((ushort)quicPort, run?.Token ?? CancellationToken.None);
            }
            else
            {
                SendBeacon();
            }

            PeerLink[] live;
            lock (Gate)
            {
                live = links.Where(link => link.Entered && !link.Closed).ToArray();
            }

            var addresses = local.Select(prefix => prefix.Address).Distinct().ToArray();
            foreach (var link in live)
            {
                link.AnnouncePaths(addresses);
            }
        }
        finally
        {
            rebuildGate.Release();
        }
    }

    internal Task RebuildNetworkForTest(IReadOnlyList<OnLinkPrefix> local) => RebuildNetworkAsync(local, force: true);

    internal Task ReconcileNetworkForTest(IReadOnlyList<OnLinkPrefix> local) => RebuildNetworkAsync(local);

    internal long LastBeaconTick => Volatile.Read(ref lastBeaconTick);

    static bool SamePrefixes(IReadOnlyList<OnLinkPrefix> left, IReadOnlyList<OnLinkPrefix> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Select(Key).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Select(Key).Order(StringComparer.Ordinal));

        static string Key(OnLinkPrefix prefix) =>
            prefix.Address + "/" + prefix.PrefixLength + "/" + prefix.InterfaceIndex + "/" + prefix.LinkLocal;
    }

    public string? Apply(LocalCommandPayload body, byte type)
    {
        if (type != FrameTypes.SendFile)
        {
            var validation = LocalOperator.Validate(type, body);
            if (validation is not null)
            {
                return validation;
            }
        }

        switch (type)
        {
            case FrameTypes.SetDisplayName:
                if (body.DisplayName is not { } displayName)
                {
                    return "invalid-name";
                }

                DisplayName = displayName;
                settings.DisplayName = displayName;
                Persist();
                discovery?.UpdateDisplayName(DisplayName);
                foreach (var link in links)
                {
                    link.RenameLocal(DisplayName);
                }

                return null;
            case FrameTypes.SetDownloadDirectory:
                settings.DownloadDirectory = body.Path ?? "";
                Downloads = SettingsStore.ResolveDownload(settings.DownloadDirectory, home);
                Directory.CreateDirectory(Downloads);
                Persist();
                return null;
            case FrameTypes.StartPairing:
                return StartPairing(body.DeviceId);
            case FrameTypes.AcceptPairing:
                return DecidePairing(body.DeviceId, link => link.AcceptFromUser());
            case FrameTypes.RejectPairing:
                return DecidePairing(body.DeviceId, link => link.RejectFromUser());
            case FrameTypes.UnpairLocal:
                if (body.DeviceId is null || !trust.ContainsKey(body.DeviceId))
                {
                    return "unknown-device";
                }

                RemoveTrust(body.DeviceId);
                return null;
            case FrameTypes.SetClipboardSync:
                return SetFlag(body.DeviceId, body.Enabled, clipboard: true);
            case FrameTypes.SetAskBeforeSave:
                return SetFlag(body.DeviceId, body.Enabled, clipboard: false);
            case FrameTypes.SendFile:
                return SendFile(body.DeviceId, body.Path);
            case FrameTypes.AcceptFile:
                return FileCommand(body.TransferId, (link, id) => link.AcceptFile(id));
            case FrameTypes.RejectFile:
                return FileCommand(body.TransferId, (link, id) => link.RejectFile(id));
            case FrameTypes.CancelTransfer:
                return FileCommand(body.TransferId, (link, id) => link.CancelFile(id));
            case FrameTypes.PushClipboardText:
                if (body.Text is null)
                {
                    return "invalid";
                }

                return PushClipboardText(body.Text);
            default:
                return "invalid";
        }
    }

    string? StartPairing(string? deviceId)
    {
        if (deviceId is null || (!seen.Contains(deviceId) && !trust.ContainsKey(deviceId)))
        {
            return "unknown-device";
        }

        if (pairingWanted.Contains(deviceId) || links.Any(link => link.RemoteId == deviceId && !link.Closed))
        {
            return "already-pending";
        }

        pairingWanted.Add(deviceId);
        return null;
    }

    string? SetFlag(string? deviceId, bool? enabled, bool clipboard)
    {
        if (deviceId is null || !trust.TryGetValue(deviceId, out var entry))
        {
            return deviceId is null ? "invalid" : "not-trusted";
        }

        if (enabled is null)
        {
            return "invalid";
        }

        if (clipboard)
        {
            entry.ClipboardSync = enabled.Value;
            Clipboard.Connect(deviceId, enabled.Value, RemoteClipboard(deviceId));
            Persist();
            Live(deviceId)?.PushClipboard();
            if (enabled.Value && RemoteClipboard(deviceId))
            {
                Console.Error.WriteLine("clipboard-mutual " + deviceId);
            }
        }
        else
        {
            entry.AskBeforeSave = enabled.Value;
            Persist();
        }

        Publish([DeviceEvent(deviceId, Presence(deviceId))]);
        return null;
    }

    string? SendFile(string? deviceId, string? path)
    {
        if (deviceId is null || !trust.ContainsKey(deviceId))
        {
            return deviceId is null ? "invalid" : "not-trusted";
        }

        if (path is null || !File.Exists(path) || Directory.Exists(path))
        {
            return "not-a-file";
        }

        if (new FileInfo(path).Length > TransferBook.MaxFileBytes)
        {
            return "too-large";
        }

        var link = Live(deviceId);
        if (link is null || !link.Entered)
        {
            return "not-connected";
        }

        return link.OfferFile(path);
    }

    string? FileCommand(string? transferId, Func<PeerLink, string, string?> act)
    {
        if (transferId is null)
        {
            return "invalid";
        }

        string? last = "unknown-transfer";
        foreach (var link in links)
        {
            if (link.Closed)
            {
                continue;
            }

            var error = act(link, transferId);
            if (error is null)
            {
                return null;
            }

            last = error;
        }

        return last;
    }

    string? DecidePairing(string? deviceId, Func<PeerLink, string?> act)
    {
        if (prompt is null || prompt.Closed || !prompt.Prompting)
        {
            return "already-decided";
        }

        if (deviceId is not null && deviceId != prompt.RemoteId)
        {
            return "unknown-device";
        }

        return act(prompt);
    }

    PeerLink? Prompt(string? deviceId)
    {
        if (prompt is null || prompt.Closed || !prompt.Prompting)
        {
            return null;
        }

        if (deviceId is not null && deviceId != prompt.RemoteId)
        {
            return null;
        }

        return prompt;
    }

    PeerLink? Live(string deviceId) =>
        links.FirstOrDefault(link => link.RemoteId == deviceId && link.Entered && !link.Closed);

    public string? PushClipboardText(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > FrameLimits.MaxClipboardBytes)
        {
            return "too-large";
        }

        ApplyLocalClipboard(text, skipIfUnchanged: false);
        return null;
    }

    void OnPlain(string text) => ApplyLocalClipboard(text, skipIfUnchanged: true);

    void ApplyLocalClipboard(string text, bool skipIfUnchanged)
    {
        List<DirectedFrame> sent = [];
        lock (Gate)
        {
            if (skipIfUnchanged && text == Applied)
            {
                return;
            }

            var previous = Applied;
            foreach (var link in links)
            {
                link.PostFrames([]);
            }

            var mutualPeers = links.Where(link => link.Entered).Select(link => link.RemoteId).ToArray();
            foreach (var id in mutualPeers)
            {
                Clipboard.Connect(id, LocalClipboard(id), RemoteClipboard(id));
            }

            sent = Clipboard.OnLocal(ClipboardKind.Plain, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ToList();
            SaveClipboard(previous);
            Applied = Clipboard.Text;
            SaveClipboard(Applied);
            foreach (var item in sent)
            {
                Live(item.PeerId)?.PostFrames([item.Frame]);
            }
        }
    }

    async Task Attach(
        IQuicConnection connection,
        X509Certificate2? peer,
        bool initiated,
        CancellationToken cancellationToken,
        string? dialedId,
        string? dialedAddress,
        byte[]? peerDer = null)
    {
        var linked = false;
        try
        {
            var der = peerDer ?? (connection as IPortableQuicConnection)?.PeerCertificateDer;
            byte[]? bytes;
            if (der is not null)
            {
                bytes = CertificateChecks.LeafSpki(der);
            }
            else
            {
                if (peer is null && !QuicLink.TryPeerCertificate(connection, out peer))
                {
                    return;
                }

                bytes = CertificateChecks.LeafSpki(peer);
            }

            if (bytes is null)
            {
                return;
            }
            var id = DeviceIds.Text(bytes);
            TrustEntry? entry;
            var address = RemoteAddress(connection);
            lock (Gate)
            {
                entry = Entry(id);
                if (!OnLinkUnlocked(address))
                {
                    return;
                }
            }

            CertificateFate fate;
            if (der is not null)
            {
                fate = CertificateChecks.Evaluate(der, entry);
            }
            else if (peer is not null)
            {
                fate = CertificateChecks.Evaluate(peer, entry);
            }
            else
            {
                return;
            }

            if (fate != CertificateFate.Continue)
            {
                return;
            }

            var stream = initiated
                ? await QuicLink.OpenSession(connection, cancellationToken).ConfigureAwait(false)
                : await QuicLink.AcceptSession(connection, cancellationToken).ConfigureAwait(false);
            var link = new PeerLink(this, connection, stream, bytes, initiated);
            lock (Gate)
            {
                links.Add(link);
                routes.NoteSession(id, address);
                seen.Add(id);
                if (dialedId is not null)
                {
                    dialing.Remove(dialedId);
                }

                linked = true;
            }

            await link.RunAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or QuicException or InvalidOperationException or AuthenticationException)
        {
            // The peer left.
        }
        finally
        {
            if (!linked && dialedId is not null)
            {
                lock (Gate)
                {
                    dialing.Remove(dialedId);
                    if (dialedAddress is not null)
                    {
                        routes.FailDial(dialedId, dialedAddress);
                    }
                }
            }

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or QuicException)
            {
                // Already closed.
            }
        }
    }

    async Task StartCustomDiscoveryAsync(IDiscovery customDiscovery, bool ownsNetwork, CancellationToken cancellationToken)
    {
        try
        {
            await customDiscovery.StartAsync(cancellationToken).ConfigureAwait(false);
            if (ownsNetwork)
            {
                await SafeRebuildAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or InvalidOperationException)
        {
            Console.Error.WriteLine("discovery-start " + ex.GetType().Name + " " + ex.Message);
        }
    }

    async Task CustomBeaconLoop(IDiscovery customDiscovery, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Volatile.Write(ref lastBeaconTick, Environment.TickCount64);
                await customDiscovery.AnnounceAsync((ushort)quicPort, cancellationToken).ConfigureAwait(false);
                await Task.Delay(BeaconIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
        {
            // The daemon stopped.
        }
    }

    void OnCustomNetworkChanged() => networkMonitor?.Signal();

    void OnCustomPeerDiscovered(DiscoveredPeer peer)
    {
        var digest = peer.Digest;
        if (digest is null || digest.Length == 0)
        {
            try
            {
                digest = Base32.DigestFromDeviceId(peer.DeviceId);
            }
            catch (FormatException)
            {
                return;
            }
        }

        if (digest.AsSpan().SequenceEqual(DeviceIds.Digest(identity.Spki)) || !IPAddress.TryParse(peer.Address, out _))
        {
            return;
        }

        NoteDiscovered(peer.DeviceId, digest, peer.Address, peer.Port, peer.DisplayName);
    }

    async Task BeaconLoop(CancellationToken cancellationToken)
    {
        if (udp is null && udp6 is null)
        {
            return;
        }

        var buffer4 = new byte[2048];
        var buffer6 = new byte[2048];
        try
        {
            if (udp is not null)
            {
                _ = ReceiveBeacons(udp, buffer4, cancellationToken);
            }

            if (udp6 is not null)
            {
                _ = ReceiveBeacons(udp6, buffer6, cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                SendBeacon();
                await Task.Delay(BeaconIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The daemon stopped.
        }
    }

    async Task ReceiveBeacons(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EndPoint remote = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
                NoteBeacon(buffer.AsSpan(0, result.ReceivedBytes), result.RemoteEndPoint);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The daemon stopped.
        }
    }

    void SendBeacon()
    {
        Volatile.Write(ref lastBeaconTick, Environment.TickCount64);
        var digest = DeviceIds.Digest(identity.Spki);
        var encoded = BeaconCodec.Encode(digest, (ushort)quicPort, DisplayName);
        if (encoded.Value is null)
        {
            return;
        }

        lock (Gate)
        {
            RefreshPrefixes();
        }

        OnLinkPrefix[] snapshot;
        lock (Gate)
        {
            snapshot = prefixes.ToArray();
        }

        if (udp is not null)
        {
            var targets = new List<IPAddress> { IPAddress.Broadcast };
            foreach (var prefix in snapshot)
            {
                if (IPAddress.TryParse(prefix.Address, out var address) && address.AddressFamily == AddressFamily.InterNetwork)
                {
                    targets.Add(Broadcast(address, prefix.PrefixLength));
                }
            }

            foreach (var target in targets.Distinct())
            {
                TrySend(udp, encoded.Value, new IPEndPoint(target, BeaconPort));
            }
        }

        if (udp6 is not null)
        {
            var interfaces = snapshot
                .Where(prefix => IPAddress.TryParse(prefix.Address, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(prefix => prefix.InterfaceIndex)
                .Where(index => index > 0)
                .Distinct();
            foreach (var index in interfaces)
            {
                try
                {
                    udp6.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, index);
                }
                catch (SocketException)
                {
                    continue;
                }

                TrySend(udp6, encoded.Value, new IPEndPoint(IPAddress.Parse("ff02::1"), BeaconPort));
            }
        }
    }

    static void TrySend(Socket socket, byte[] payload, IPEndPoint target)
    {
        try
        {
            socket.SendTo(payload, target);
        }
        catch (SocketException)
        {
            // This interface is not up.
        }
    }

    void NoteBeacon(ReadOnlySpan<byte> bytes, EndPoint? remote)
    {
        var beacon = BeaconCodec.Decode(bytes);
        if (beacon.IsFailure || beacon.Value is null || remote is not IPEndPoint end)
        {
            return;
        }

        var digest = beacon.Value.Digest;
        if (digest.AsSpan().SequenceEqual(DeviceIds.Digest(identity.Spki)))
        {
            return;
        }

        NoteDiscovered("ca1" + Base32.Encode(digest), digest, LocalNetwork.Text(end.Address), beacon.Value.Port, beacon.Value.Name);
    }

    public void NoteMdns(MdnsPeer peer)
    {
        byte[] digest;
        try
        {
            digest = Base32.DigestFromDeviceId(peer.DeviceId);
        }
        catch (FormatException)
        {
            return;
        }

        if (digest.AsSpan().SequenceEqual(DeviceIds.Digest(identity.Spki)) || !IPAddress.TryParse(peer.Address, out _))
        {
            return;
        }

        NoteDiscovered(peer.DeviceId, digest, peer.Address, peer.Port, peer.DisplayName);
    }

    void NoteDiscovered(string id, byte[] digest, string address, int port, string? name)
    {
        lock (Gate)
        {
            if (disposed)
            {
                return;
            }

            seen.Add(id);
            routes.AddDiscovery(id, digest, address, port, Environment.TickCount64, name);
            if (trust.ContainsKey(id))
            {
                routes.RememberTrust(id);
            }

            var presence = Presence(id);
            if (announcedPresence.TryGetValue(id, out var previous) && previous == presence)
            {
                return;
            }

            announcedPresence[id] = presence;
            Publish([DeviceEvent(id, presence)]);
        }
    }

    async Task DialLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                List<(string Id, string Address, int Port)> targets = [];
                lock (Gate)
                {
                    routes.Expire(Environment.TickCount64);
                    foreach (var id in seen.Concat(trust.Keys).Distinct())
                    {
                        if (!routes.ShouldDial(id, pairingWanted.Contains(id)))
                        {
                            dialing.Remove(id);
                            continue;
                        }

                        if (!dialing.Add(id))
                        {
                            continue;
                        }

                        var candidate = routes.NextDialCandidate(id);
                        if (candidate is null)
                        {
                            dialing.Remove(id);
                            continue;
                        }

                        targets.Add((id, candidate.Address, candidate.Port));
                    }
                }

                foreach (var target in targets)
                {
                    if (!IPAddress.TryParse(target.Address, out var address))
                    {
                        lock (Gate)
                        {
                            dialing.Remove(target.Id);
                        }

                        continue;
                    }

                    try
                    {
                        using var dial = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        dial.CancelAfter(TimeSpan.FromSeconds(4));
                        var (connection, peerCert, peerDer) = await ConnectMutualInternalAsync(new IPEndPoint(address, target.Port), dial.Token).ConfigureAwait(false);
                        _ = Attach(connection, peerCert, initiated: true, cancellationToken, target.Id, target.Address, peerDer);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException or QuicException or InvalidOperationException or SocketException or AuthenticationException)
                    {
                        lock (Gate)
                        {
                            dialing.Remove(target.Id);
                            routes.FailDial(target.Id, target.Address);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The daemon stopped.
        }
    }

    Frame SnapshotFrame()
    {
        var prompts = new List<FilePromptSnapshotPayload>();
        var transfers = new List<TransferSnapshotPayload>();
        foreach (var link in links)
        {
            link.Collect(prompts, transfers);
        }

        var devices = seen.Concat(trust.Keys).Distinct()
            .Select(id => new DeviceSnapshotPayload(
                id,
                NameOf(id),
                trust.ContainsKey(id),
                Presence(id),
                trust.ContainsKey(id) ? trust[id].ClipboardSync : null,
                trust.ContainsKey(id) ? trust[id].AskBeforeSave : null,
                trust.ContainsKey(id) ? RemoteClipboard(id) : null,
                EndpointOf(id)))
            .ToArray();
        var pairing = prompt is { Prompting: true }
            ? new PairingSnapshotPayload(prompt.RemoteId, prompt.RemoteName, prompt.Code ?? "", prompt.RemainingMs)
            : null;
        return FrameMessages.Json(
            FrameTypes.Snapshot,
            0,
            0,
            FrameTypes.SessionStream,
            new SnapshotPayload(
                DisplayName,
                settings.DownloadDirectory,
                devices,
                pairing,
                prompts.ToArray(),
                transfers.ToArray(),
                LocalDeviceId,
                Downloads,
                LocalAddresses(),
                quicPort,
                ClipboardBackend),
            ProtocolJsonContext.Default.SnapshotPayload);
    }

    DeviceEventPayload Describe(string deviceId, string presence) =>
        new(
            deviceId,
            NameOf(deviceId),
            trust.ContainsKey(deviceId),
            presence,
            trust.ContainsKey(deviceId) ? trust[deviceId].ClipboardSync : null,
            trust.ContainsKey(deviceId) ? trust[deviceId].AskBeforeSave : null,
            trust.ContainsKey(deviceId) ? RemoteClipboard(deviceId) : null,
            EndpointOf(deviceId));

    string? EndpointOf(string deviceId) =>
        Live(deviceId)?.EndpointText ?? routes.PreferredEndpointOf(deviceId);

    string Presence(string deviceId)
    {
        if (links.Any(link => link.RemoteId == deviceId && link.Entered && !link.Closed))
        {
            return "connected";
        }

        return routes.DialCandidates(deviceId).Count > 0 || routes.DiscoveryPaths(deviceId).Count > 0
            ? "reachable"
            : "offline";
    }

    string NameOf(string deviceId)
    {
        if (trust.TryGetValue(deviceId, out var entry) && !string.IsNullOrEmpty(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        return routes.DisplayNameOf(deviceId) ?? deviceId;
    }

    TrustEntry? Entry(string deviceId) =>
        trust.TryGetValue(deviceId, out var entry)
            ? new TrustEntry(entry.DeviceId, entry.DisplayName, entry.Spki, entry.ClipboardSync, entry.AskBeforeSave)
            : null;

    void Persist() => SettingsStore.Write(settingsPath, settings);

    void RefreshPrefixes()
    {
        prefixes.Clear();
        if (discovery?.GetActivePrefixes() is { } owned)
        {
            prefixes.AddRange(owned);
            return;
        }

        prefixes.AddRange(FirewallProfiles.Filter(LocalNetwork.Enumerate(), firewall));
    }

    internal IReadOnlyList<OnLinkPrefix> LastNetworkForTest => lastNetwork;

    bool OnLinkUnlocked(string address)
    {
        RefreshPrefixes();
        return prefixes.Any(prefix => SameLink(address, prefix));
    }

    static bool SameLink(string address, OnLinkPrefix prefix)
    {
        if (!IPAddress.TryParse(address, out var ip) || !IPAddress.TryParse(prefix.Address, out var network))
        {
            return false;
        }

        var left = ip.GetAddressBytes();
        var right = network.GetAddressBytes();
        if (left.Length != right.Length)
        {
            return false;
        }

        var bits = prefix.PrefixLength;
        for (var i = 0; i < left.Length && bits > 0; i++)
        {
            var take = Math.Min(8, bits);
            var mask = (byte)(0xFF << (8 - take));
            if ((left[i] & mask) != (right[i] & mask))
            {
                return false;
            }

            bits -= take;
        }

        return true;
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

    static string RemoteAddress(IQuicConnection connection)
    {
        if (connection.RemoteEndPoint is not IPEndPoint end)
        {
            return "";
        }

        var address = end.Address.IsIPv4MappedToIPv6 ? end.Address.MapToIPv4() : end.Address;
        return address.ToString();
    }

    async Task<(IQuicConnection Connection, X509Certificate2? PeerCert, byte[]? PeerDer)> ConnectMutualInternalAsync(
        IPEndPoint remote,
        CancellationToken cancellationToken)
    {
        if (QuicLink.Backend is IPortableQuicBackend portable && identity.Portable is { } credential)
        {
            var (connection, peerDer) = await portable.ConnectMutualPortableAsync(remote, credential, cancellationToken).ConfigureAwait(false);
            return (connection, null, peerDer);
        }

        var certificate = CertificateFor(identity.Pem);
        var connected = await QuicLink.Backend.ConnectMutualAsync(remote, certificate, cancellationToken).ConfigureAwait(false);
        return (connected.Connection, connected.Peer, null);
    }

    static X509Certificate2 CertificateFor(string pem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        var request = new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256);
        using var issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var exported = issued.Export(X509ContentType.Pfx, "contextall");
        return X509CertificateLoader.LoadPkcs12(exported, "contextall", X509KeyStorageFlags.Exportable);
    }

    internal bool UsesBuiltInDiscovery => mdns is not null || udp is not null || udp6 is not null;

    static Frame PromptFrame(PeerLink link) =>
        FrameMessages.Json(
            FrameTypes.PairingPrompt,
            0,
            0,
            FrameTypes.SessionStream,
            new PairingSnapshotPayload(link.RemoteId, link.RemoteName, link.Code ?? "", link.RemainingMs),
            ProtocolJsonContext.Default.PairingSnapshotPayload);

    static Frame Ended(string deviceId, string outcome) =>
        FrameMessages.Json(FrameTypes.PairingEnded, 0, 0, FrameTypes.SessionStream, new PairingEndedPayload(deviceId, outcome), ProtocolJsonContext.Default.PairingEndedPayload);
}
