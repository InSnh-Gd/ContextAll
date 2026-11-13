using ContextAll.Core;
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

    public LanHost(SessionHost engine)
    {
        this.engine = engine;
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
        var paths = new LinuxHostPaths();
        var clipboard = LinuxClipboardSource.TryOpen(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
        var sink = new LinuxTransferSink(paths.DefaultDownloadDirectory);
        try
        {
            var engine = new SessionHost(paths, clipboard, discovery: null, transferSink: sink);
            return new LanHost(engine);
        }
        catch
        {
            return null;
        }
    }

    internal static LanHost CreateIsolated(string root) => new(SessionHost.CreateIsolated(root));

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

            var error = engine.Apply(body, frame.Type);
            client.Post(Result(body.RequestId, error is null, error));
        }

        engine.FlushClipboard();
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
    internal long LastBeaconTick => engine.LastBeaconTick;

    public void Dispose() => engine.Dispose();

    static bool RequestIdOk(string id) =>
        id.Length is >= 1 and <= 64 && id.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    static Frame Result(string requestId, bool ok, string? error) =>
        FrameMessages.Json(FrameTypes.CommandResult, 0, 0, FrameTypes.SessionStream, new CommandResultPayload(requestId, ok, error), ProtocolJsonContext.Default.CommandResultPayload);
}
