using System.Net;
using System.Net.NetworkInformation;
using System.Net.Quic;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;

namespace ContextAll.Host;

public sealed class LanHost : IDisposable
{
    public const int BeaconPort = 42462;

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
    readonly List<FrontClient> clients = [];
    readonly List<OnLinkPrefix> prefixes = [];
    readonly CancellationTokenSource stop = new();
    CancellationTokenSource? run;
    readonly RouteTable routes;
    readonly List<QuicListener> listeners = [];
    Socket? udp;
    WaylandClipboard? wayland;
    PeerLink? prompt;
    string? pendingText;
    bool pendingWrite;
    bool disposed;

    LanHost(IdentityMaterial identity, string settingsPath, string home, SettingsDocument settings, string stateDirectory)
    {
        this.identity = identity;
        this.settingsPath = settingsPath;
        this.home = home;
        this.settings = settings;
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

    public byte[] LocalSpki { get; }

    public ClipboardNode Clipboard { get; }

    public TransferStore Store { get; }

    public string Downloads { get; private set; }

    public string NotifyLog { get; }

    public string Applied { get; set; }

    public string ClipboardPath { get; }

    public static LanHost? TryCreate()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Path.GetTempPath();
        }

        var identity = IdentityStore.Start(LinuxPaths.IdentityDirectory(Environment.GetEnvironmentVariable("XDG_DATA_HOME"), home));
        if (!identity.Ok || identity.Material is null)
        {
            return null;
        }

        var settingsPath = LinuxPaths.SettingsFile(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), home);
        var settings = SettingsStore.Start(settingsPath, DisplayNames.Initial(Environment.MachineName));
        if (!settings.Ok || settings.Document is null)
        {
            return null;
        }

        var state = LinuxPaths.StateDirectory(Environment.GetEnvironmentVariable("XDG_STATE_HOME"), home);
        Directory.CreateDirectory(state);
        return new LanHost(identity.Material, settingsPath, home, settings.Document, state);
    }

    public void Start(CancellationToken cancellationToken)
    {
        run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
        var token = run.Token;
        wayland = WaylandClipboard.TryOpen(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
        Console.Error.WriteLine(wayland is { IsOpen: true } ? "clipboard-ready" : "clipboard-disabled");
        if (wayland is not null)
        {
            wayland.PlainText += OnPlain;
        }

        _ = Task.Run(() => ListenAsync(token), token);

        try
        {
            udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.EnableBroadcast = true;
            udp.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));
        }
        catch (SocketException)
        {
            udp?.Dispose();
            udp = null;
            Console.Error.WriteLine("beacon-skipped");
        }

        _ = Task.Run(() => BeaconLoop(token), token);
        _ = Task.Run(() => DialLoop(token), token);
    }

    public void Join(FrontClient client)
    {
        lock (Gate)
        {
            clients.Add(client);
        }
    }

    public void Leave(FrontClient client)
    {
        lock (Gate)
        {
            clients.Remove(client);
        }
    }

    public Frame Snapshot()
    {
        lock (Gate)
        {
            return SnapshotFrame();
        }
    }

    public void Command(FrontClient client, Frame frame)
    {
        if (frame.Type is < FrameTypes.StartPairing or > FrameTypes.CancelTransfer)
        {
            return;
        }

        lock (Gate)
        {
            var command = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.LocalCommandPayload);
            if (command.IsFailure || command.Value is null || !RequestIdOk(command.Value.RequestId))
            {
                client.Post(Result(command.Value?.RequestId ?? "0", false, "invalid"));
                return;
            }

            var body = command.Value;
            if (!client.Requests.Add(body.RequestId))
            {
                client.Post(Result(body.RequestId, false, "duplicate-request"));
                return;
            }

            var error = Apply(body, frame.Type);
            client.Post(Result(body.RequestId, error is null, error));
        }

        FlushClipboard();
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
            live = notify ? Live(deviceId) : null;
        }

        live?.SendUnpair();
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
        lock (Gate)
        {
            foreach (var client in clients)
            {
                foreach (var frame in frames)
                {
                    client.Post(frame);
                }
            }
        }
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

        wayland?.SetText(text);
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
        wayland?.Dispose();
        udp?.Dispose();
        foreach (var item in listeners)
        {
            _ = item.DisposeAsync().AsTask();
        }

        run?.Dispose();
    }

    async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var certificate = CertificateFor(identity.Pem);
            var spki = CertificateChecks.LeafSpki(certificate);
            var matches = spki is not null && spki.AsSpan().SequenceEqual(identity.Spki);
            if (!matches)
            {
                Console.Error.WriteLine("quic-skipped spki");
                return;
            }

            List<IPAddress> addresses;
            lock (Gate)
            {
                RefreshPrefixes();
                addresses = [];
                foreach (var prefix in prefixes)
                {
                    if (IPAddress.TryParse(prefix.Address, out var address))
                    {
                        addresses.Add(address);
                    }
                }
            }

            foreach (var endpoint in QuicLink.ListenerEndpoints(addresses))
            {
                try
                {
                    listeners.Add(await QuicLink.ListenMutual(endpoint.Address, certificate, endpoint.Port, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is SocketException or InvalidOperationException or QuicException or IOException or AuthenticationException)
                {
                    Console.Error.WriteLine("quic-skipped " + endpoint.Address + " " + ex.GetType().Name + " " + ex.Message);
                }
            }

            if (listeners.Count == 0)
            {
                Console.Error.WriteLine(QuicLink.Supported ? "quic-skipped no-beacon-address" : "quic-skipped quic-unsupported");
                return;
            }

            await AcceptLoop(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or QuicException or IOException or CryptographicException or AuthenticationException or OperationCanceledException)
        {
            Console.Error.WriteLine("quic-skipped " + ex.GetType().Name + " " + ex.Message);
        }
    }

    string? Apply(LocalCommandPayload body, byte type)
    {
        switch (type)
        {
            case FrameTypes.SetDisplayName:
                if (body.DisplayName is null || !DisplayNames.IsLegal(body.DisplayName))
                {
                    return "invalid-name";
                }

                DisplayName = body.DisplayName;
                settings.DisplayName = body.DisplayName;
                Persist();
                foreach (var link in links)
                {
                    if (link.Entered)
                    {
                        link.PostFrames([FrameMessages.Json(FrameTypes.DisplayName, 0, 0, FrameTypes.SessionStream, new DisplayNamePayload(DisplayName), ProtocolJsonContext.Default.DisplayNamePayload)]);
                    }
                }

                return null;
            case FrameTypes.SetDownloadDirectory:
                if (body.Path is { Length: > 0 } && !Directory.Exists(body.Path))
                {
                    return "invalid-path";
                }

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

        if (new FileInfo(path).Length > 1_000_000_000)
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

    void OnPlain(string text)
    {
        List<DirectedFrame> sent = [];
        lock (Gate)
        {
            if (text == Applied)
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

    async Task AcceptLoop(CancellationToken cancellationToken)
    {
        var waits = new List<(QuicListener Listener, Task<QuicConnection> Accept)>();
        foreach (var item in listeners)
        {
            waits.Add((item, item.AcceptConnectionAsync(cancellationToken).AsTask()));
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested && waits.Count > 0)
            {
                var finished = await Task.WhenAny(waits.Select(item => item.Accept)).ConfigureAwait(false);
                var index = waits.FindIndex(item => item.Accept == finished);
                var slot = waits[index];
                waits.RemoveAt(index);
                var connection = await slot.Accept.ConfigureAwait(false);
                _ = Attach(connection, null, initiated: false, cancellationToken, null, null);
                waits.Add((slot.Listener, slot.Listener.AcceptConnectionAsync(cancellationToken).AsTask()));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or QuicException)
        {
            // The daemon stopped.
        }
    }

    async Task Attach(
        QuicConnection connection,
        X509Certificate2? peer,
        bool initiated,
        CancellationToken cancellationToken,
        string? dialedId,
        string? dialedAddress)
    {
        var linked = false;
        try
        {
            if (peer is null && !QuicLink.TryPeerCertificate(connection, out peer))
            {
                return;
            }

            var bytes = CertificateChecks.LeafSpki(peer);
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

            if (CertificateChecks.Evaluate(peer, entry) != CertificateFate.Continue)
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

    async Task BeaconLoop(CancellationToken cancellationToken)
    {
        if (udp is null)
        {
            return;
        }

        var buffer = new byte[2048];
        try
        {
            _ = ReceiveBeacons(buffer, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                SendBeacon();
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The daemon stopped.
        }
    }

    async Task ReceiveBeacons(byte[] buffer, CancellationToken cancellationToken)
    {
        if (udp is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                var result = await udp.ReceiveFromAsync(buffer, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);
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
        if (udp is null)
        {
            return;
        }

        var digest = DeviceIds.Digest(identity.Spki);
        var encoded = BeaconCodec.Encode(digest, QuicLink.Port, DisplayName);
        if (encoded.Value is null)
        {
            return;
        }

        lock (Gate)
        {
            RefreshPrefixes();
        }

        var targets = new List<IPAddress> { IPAddress.Broadcast };
        lock (Gate)
        {
            foreach (var prefix in prefixes)
            {
                if (IPAddress.TryParse(prefix.Address, out var address))
                {
                    targets.Add(Broadcast(address, prefix.PrefixLength));
                }
            }
        }

        foreach (var target in targets.Distinct())
        {
            try
            {
                udp.SendTo(encoded.Value, new IPEndPoint(target, BeaconPort));
            }
            catch (SocketException)
            {
                // This interface is not up.
            }
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

        var address = end.Address.IsIPv4MappedToIPv6 ? end.Address.MapToIPv4() : end.Address;
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return;
        }

        var id = "ca1" + Base32.Encode(digest);
        lock (Gate)
        {
            seen.Add(id);
            routes.AddDiscovery(id, digest, address.ToString(), beacon.Value.Port, Environment.TickCount64, beacon.Value.Name);
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

                        var candidate = routes.DialCandidates(id).FirstOrDefault();
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
                        var certificate = CertificateFor(identity.Pem);
                        var connected = await QuicLink.ConnectMutual(new IPEndPoint(address, target.Port), certificate, dial.Token).ConfigureAwait(false);
                        _ = Attach(connected.Connection, connected.Peer, initiated: true, cancellationToken, target.Id, target.Address);
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
                trust.ContainsKey(id) ? trust[id].AskBeforeSave : null))
            .ToArray();
        var pairing = prompt is { Prompting: true }
            ? new PairingSnapshotPayload(prompt.RemoteId, prompt.RemoteName, prompt.Code ?? "", prompt.RemainingMs)
            : null;
        return FrameMessages.Json(
            FrameTypes.Snapshot,
            0,
            0,
            FrameTypes.SessionStream,
            new SnapshotPayload(DisplayName, settings.DownloadDirectory, devices, pairing, prompts.ToArray(), transfers.ToArray()),
            ProtocolJsonContext.Default.SnapshotPayload);
    }

    DeviceEventPayload Describe(string deviceId, string presence) =>
        new(
            deviceId,
            NameOf(deviceId),
            trust.ContainsKey(deviceId),
            presence,
            trust.ContainsKey(deviceId) ? trust[deviceId].ClipboardSync : null,
            trust.ContainsKey(deviceId) ? trust[deviceId].AskBeforeSave : null);

    string Presence(string deviceId)
    {
        if (links.Any(link => link.RemoteId == deviceId && link.Entered && !link.Closed))
        {
            return "connected";
        }

        return routes.DialCandidates(deviceId).Count > 0 || routes.DiscoveryPaths(deviceId).Count > 0
            ? "available"
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
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                prefixes.Add(new OnLinkPrefix(unicast.Address.ToString(), unicast.PrefixLength));
            }
        }
    }

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

    static string RemoteAddress(QuicConnection connection)
    {
        if (connection.RemoteEndPoint is not IPEndPoint end)
        {
            return "";
        }

        var address = end.Address.IsIPv4MappedToIPv6 ? end.Address.MapToIPv4() : end.Address;
        return address.ToString();
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

    static bool RequestIdOk(string id) =>
        id.Length is >= 1 and <= 64 && id.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    static Frame Result(string requestId, bool ok, string? error) =>
        FrameMessages.Json(FrameTypes.CommandResult, 0, 0, FrameTypes.SessionStream, new CommandResultPayload(requestId, ok, error), ProtocolJsonContext.Default.CommandResultPayload);

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
