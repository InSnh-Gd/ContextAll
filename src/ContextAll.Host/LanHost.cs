using System.Net;
using System.Net.Sockets;
using System.Text;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class LanHost : IDisposable
{
    public const int BeaconPort = SessionHost.BeaconPort;
    internal const int BeaconIntervalMs = SessionHost.BeaconIntervalMs;
    internal const int ReconcileSeconds = SessionHost.ReconcileSeconds;

    readonly SessionHost engine;
    readonly List<FrontClient> clients = [];
    readonly ILinuxNotificationSink? notificationSink;
    readonly LinuxNotificationCoordinator? notifications;

    public LanHost(
        SessionHost engine,
        ILinuxNotificationSink? notificationSink = null,
        string? runtimeDirectory = null,
        Func<CancellationToken, Task>? activateShellOverride = null)
    {
        this.engine = engine;
        this.notificationSink = notificationSink;
        if (notificationSink is not null)
        {
            notifications = new LinuxNotificationCoordinator(
                engine,
                notificationSink,
                runtimeDirectory,
                activateShellOverride);
        }

        this.engine.Published += Publish;
    }

    public SessionHost Engine => engine;

    public static implicit operator SessionHost(LanHost host) => host.engine;

    public object Gate => engine.Gate;
    public string DisplayName => engine.DisplayName;
    public byte[] LocalSpki => engine.LocalSpki;
    public ClipboardNode Clipboard => engine.Clipboard;
    public TransferStore Store => engine.Store;
    public string Downloads => engine.Downloads;
    public string NotifyLog => engine.NotifyLog;
    public string Applied { get => engine.Applied; set => engine.Applied = value; }
    public string ClipboardPath => engine.ClipboardPath;

    public static LanHost? TryCreate()
    {
        var paths = HostPaths.ForThisPlatform();
        var sink = new LinuxTransferSink(paths.DefaultDownloadDirectory);
        IClipboardSource? clipboard = null;
        try
        {
            var identity = IdentityStore.Start(paths.IdentityDirectory);
            if (!identity.Ok || identity.Material is null)
            {
                return null;
            }

            var settings = SettingsStore.Start(paths.SettingsFile, DisplayNames.Initial(Environment.MachineName));
            var displayName = settings.Document?.DisplayName;
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = DisplayNames.Initial(Environment.MachineName);
            }

            var discovery = CreateLinuxDiscovery(identity.Material, displayName, QuicLink.Port);
            clipboard = OperatingSystem.IsWindows()
                ? WindowsClipboardSource.TryOpen()
                : LinuxClipboardSource.TryOpen(
                    Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                    Environment.GetEnvironmentVariable("DISPLAY"));
            var engine = new SessionHost(
                paths,
                clipboard,
                discovery,
                transferSink: sink,
                firewall: WindowsFirewall.ForThisPlatform(),
                preloadedIdentity: identity.Material);
            return new LanHost(
                engine,
                DbusNotificationSink.TryCreate(),
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
        }
        catch
        {
            clipboard?.Dispose();
            return null;
        }
    }

    internal static LanHost CreateIsolated(string root) => new(SessionHost.CreateIsolated(root));

    internal static IDiscovery? CreateLinuxDiscovery(IdentityMaterial identity, string displayName, int quicPort)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        return new LinuxDiscovery(
            identity.DeviceId,
            DeviceIds.Digest(identity.Spki),
            displayName,
            (ushort)quicPort);
    }

    public void Start(CancellationToken cancellationToken) => engine.Start(cancellationToken);

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

    public Frame Snapshot() => engine.Snapshot();

    public void Command(FrontClient client, Frame frame)
    {
        if (!LocalFrameTypes.IsCommand(frame.Type))
        {
            return;
        }

        LocalCommandPayload? connectBody = null;
        lock (Gate)
        {
            var command = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.LocalCommandPayload);
            if (command.IsFailure || command.Value is null)
            {
                client.Post(Result(command.Value?.RequestId ?? "0", false, "invalid"));
                return;
            }

            var body = command.Value;
            var admit = LocalOperator.Admit(body.RequestId, client.Requests);
            if (admit is not null)
            {
                client.Post(Result(body.RequestId, false, admit));
                return;
            }

            if (frame.Type == FrameTypes.ConnectEndpoint)
            {
                var validation = LocalOperator.ValidateConnectEndpoint(body);
                if (validation is not null)
                {
                    client.Post(Result(body.RequestId, false, validation));
                    return;
                }

                connectBody = body;
            }
            else if (frame.Type == FrameTypes.PushClipboardText)
            {
                var pushError = HandleClipboardPush(client, body);
                client.Post(Result(body.RequestId, pushError is null, pushError));
            }
            else if (engine.ClipboardHistoryCommand(body, frame.Type) is { } historyResult)
            {
                client.Post(FrameMessages.Json(FrameTypes.CommandResult, 0, 0, FrameTypes.SessionStream,
                    historyResult, ProtocolJsonContext.Default.CommandResultPayload));
            }
            else
            {
                var error = engine.Apply(body, frame.Type);
                if (error is null)
                {
                    notifications?.NotifyLocalDecision(frame.Type, body);
                }

                client.Post(Result(body.RequestId, error is null, error));
            }
        }

        if (connectBody is not null)
        {
            _ = ConnectAndReplyAsync(client, connectBody);
            return;
        }

        engine.FlushClipboard();
    }

    string? HandleClipboardPush(FrontClient client, LocalCommandPayload body)
    {
        var validation = LocalOperator.ValidatePushClipboard(body);
        if (validation is not null)
        {
            client.PendingPush = null;
            return validation;
        }

        if (body.Text is not null)
        {
            client.PendingPush = null;
            return engine.PushClipboardText(body.Text);
        }

        if (body.ChunkBase64 is null || body.TotalBytes is not int total || body.Offset is not int offset)
        {
            client.PendingPush = null;
            return "invalid";
        }

        var decoded = new byte[(body.ChunkBase64.Length * 3 + 3) / 4];
        if (!Convert.TryFromBase64String(body.ChunkBase64, decoded, out var written) || written == 0 || offset + written > total)
        {
            client.PendingPush = null;
            return "invalid";
        }

        if (offset == 0)
        {
            client.PendingPush = new PendingClipboardPush { Buffer = new byte[total] };
        }
        else if (client.PendingPush is null || client.PendingPush.Buffer.Length != total || offset != client.PendingPush.Received)
        {
            client.PendingPush = null;
            return "invalid";
        }

        var pending = client.PendingPush;
        decoded.AsSpan(0, written).CopyTo(pending.Buffer.AsSpan(offset, written));
        pending.Received = offset + written;
        if (pending.Received < total)
        {
            return null;
        }

        client.PendingPush = null;
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(pending.Buffer);
            return engine.PushClipboardText(text);
        }
        catch (DecoderFallbackException)
        {
            return "invalid";
        }
    }

    async Task ConnectAndReplyAsync(FrontClient client, LocalCommandPayload body)
    {
        if (body.Host is null || body.Port is not int port)
        {
            client.Post(Result(body.RequestId, false, "invalid-endpoint"));
            return;
        }

        var hostText = body.Host.Trim();
        IPAddress? address;
        if (IPAddress.TryParse(hostText, out var parsed))
        {
            address = parsed;
        }
        else
        {
            try
            {
                var candidates = await Dns.GetHostAddressesAsync(hostText).ConfigureAwait(false);
                address = candidates.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? candidates.FirstOrDefault();
            }
            catch (SocketException)
            {
                address = null;
            }

            if (address is null)
            {
                client.Post(Result(body.RequestId, false, "invalid-endpoint"));
                return;
            }
        }

        var outcome = await engine.ConnectEndpointAsync(new IPEndPoint(address, port), body.Alias).ConfigureAwait(false);
        client.Post(Result(body.RequestId, outcome.Ok, outcome.Error));
    }

    public void Publish(IReadOnlyList<Frame> frames)
    {
        FrontClient[] copy;
        lock (Gate)
        {
            copy = clients.ToArray();
        }

        foreach (var frame in frames)
        {
            foreach (var client in copy)
            {
                client.Post(frame);
            }
        }
    }

    public bool IsTrusted(string deviceId) => engine.IsTrusted(deviceId);
    public bool Mutual(string deviceId) => engine.Mutual(deviceId);
    public bool LocalClipboard(string deviceId) => engine.LocalClipboard(deviceId);
    public bool RemoteClipboard(string deviceId) => engine.RemoteClipboard(deviceId);
    public bool Ask(string deviceId) => engine.Ask(deviceId);
    public HybridClock? RemoteClock(string deviceId) => engine.RemoteClock(deviceId);
    public void NoteRemoteClipboard(string deviceId, bool enabled) => engine.NoteRemoteClipboard(deviceId, enabled);
    public void RememberRemoteClock(string deviceId, HybridClock clock) => engine.RememberRemoteClock(deviceId, clock);
    public void RememberBytes(string transferId, byte[] data) => engine.RememberBytes(transferId, data);
    public bool TryBytes(string transferId, out byte[] data) => engine.TryBytes(transferId, out data);
    public void ForgetBytes(string transferId) => engine.ForgetBytes(transferId);
    public void SaveTrust(TrustEntry entry) => engine.SaveTrust(entry);
    public void RemoveTrust(string deviceId, bool notify = true) => engine.RemoveTrust(deviceId, notify);
    public void RenamePeer(string deviceId, string name) => engine.RenamePeer(deviceId, name);
    public void NoteAnnouncement(string deviceId, string[] addresses) => engine.NoteAnnouncement(deviceId, addresses);
    public string[] LocalAddresses() => engine.LocalAddresses();
    public void Settle(PeerLink link) => engine.Settle(link);
    public bool OtherLive(PeerLink link) => engine.OtherLive(link);
    public void ShowPrompt(PeerLink link) => engine.ShowPrompt(link);
    public void EndPairing(PeerLink link, string outcome) => engine.EndPairing(link, outcome);
    public void OnEntered(PeerLink link) => engine.OnEntered(link);
    public void LinkClosed(PeerLink link) => engine.LinkClosed(link);
    public void Track(PeerLink link) => engine.Track(link);
    internal void NoteMdns(MdnsPeer peer) => engine.NoteMdns(peer);
    public Frame DeviceEvent(string deviceId, string presence) => engine.DeviceEvent(deviceId, presence);
    public Frame WithSyncFlag(Frame frame, bool enabled) => engine.WithSyncFlag(frame, enabled);
    public void SaveClipboard(string applied) => engine.SaveClipboard(applied);
    public void WriteClipboard(string text) => engine.WriteClipboard(text);
    public void FlushClipboard() => engine.FlushClipboard();
    internal Task RebuildNetworkForTest(IReadOnlyList<OnLinkPrefix> local) => engine.RebuildNetworkForTest(local);
    internal Task ReconcileNetworkForTest(IReadOnlyList<OnLinkPrefix> local) => engine.ReconcileNetworkForTest(local);
    internal IReadOnlyList<OnLinkPrefix> LastNetworkForTest => engine.LastNetworkForTest;
    internal long LastBeaconTick => engine.LastBeaconTick;

    public void Dispose()
    {
        engine.Published -= Publish;
        notifications?.Dispose();
        notificationSink?.Dispose();
        engine.Dispose();
    }

    static Frame Result(string requestId, bool ok, string? error) =>
        FrameMessages.Json(FrameTypes.CommandResult, 0, 0, FrameTypes.SessionStream, new CommandResultPayload(requestId, ok, error), ProtocolJsonContext.Default.CommandResultPayload);
}
