using System.Text.Json.Serialization.Metadata;
using ContextAll.Protocol;

namespace ContextAll.Core;

public sealed class PairingSession
{
    readonly byte[] localSpki;
    readonly byte[] remoteSpki;
    readonly byte[] initiatorSpki;
    readonly List<Frame> frames = [];
    bool helloSent;

    PairingSession(byte[] localSpki, byte[] remoteSpki, byte[] initiatorSpki, string displayName)
    {
        this.localSpki = localSpki;
        this.remoteSpki = remoteSpki;
        this.initiatorSpki = initiatorSpki;
        HelloName = displayName;
        CurrentName = displayName;
    }

    public string HelloName { get; }

    public string CurrentName { get; private set; }

    public bool RemoteHelloSeen { get; private set; }

    public bool Closed { get; private set; }

    public bool Trusted { get; private set; }

    public bool IsSurvivor { get; private set; }

    public string? ShortCode { get; private set; }

    public long ReadyAt { get; private set; }

    public List<TrustEntry> Trust { get; } = [];

    public IReadOnlyList<Frame> Frames => frames;

    public byte[] InitiatorSpki => initiatorSpki;

    public static PairingSession Open(byte[] localSpki, byte[] remoteSpki, byte[] initiatorSpki, string displayName) =>
        new(localSpki.ToArray(), remoteSpki.ToArray(), initiatorSpki.ToArray(), displayName);

    public void OnWritable()
    {
        if (helloSent || Closed)
        {
            return;
        }

        helloSent = true;
        frames.Add(Json(FrameTypes.Hello, 0, FrameTypes.SessionStream, new HelloPayload("pairing", HelloName), ProtocolJsonContext.Default.HelloPayload));
    }

    public string RemoteName { get; private set; } = "";

    public void ReceiveHello(string displayName)
    {
        RemoteHelloSeen = true;
        RemoteName = displayName;
    }

    public static void Deduplicate(PairingSession left, PairingSession right)
    {
        if (!left.helloSent || !right.helloSent || !left.RemoteHelloSeen || !right.RemoteHelloSeen)
        {
            return;
        }

        var order = DeviceIds.Compare(DeviceIds.Digest(left.initiatorSpki), DeviceIds.Digest(right.initiatorSpki));
        var keep = order >= 0 ? left : right;
        var drop = ReferenceEquals(keep, left) ? right : left;
        keep.BecomeSurvivor();
        drop.BecomeDuplicate();
    }

    public void OnClock(long nowMs)
    {
        if (!IsSurvivor || Trusted || Closed)
        {
            return;
        }

        if (nowMs - ReadyAt >= 60_000)
        {
            Closed = true;
        }
    }

    public void Reject()
    {
        if (Closed)
        {
            return;
        }

        frames.Add(Json(FrameTypes.PairReject, 0, FrameTypes.SessionStream, new DonePayload(false), ProtocolJsonContext.Default.DonePayload));
        Closed = true;
    }

    public void AdoptIfOnly()
    {
        if (!helloSent || !RemoteHelloSeen || Closed || IsSurvivor)
        {
            return;
        }

        BecomeSurvivor();
    }

    public void AcceptLocal()
    {
        if (Closed || !IsSurvivor)
        {
            return;
        }

        if (Trust.Count > 0)
        {
            return;
        }

        LocalAccepted = true;
        TryStoreTrust();
    }

    public void AcceptRemote()
    {
        if (Closed || !IsSurvivor)
        {
            return;
        }

        RemoteAccepted = true;
        TryStoreTrust();
    }

    public bool LocalAccepted { get; private set; }

    public bool RemoteAccepted { get; private set; }

    public void Rename(string name)
    {
        CurrentName = name;
        if (!Trusted || name == HelloName)
        {
            return;
        }

        frames.Add(Json(FrameTypes.DisplayName, 0, FrameTypes.SessionStream, new DisplayNamePayload(name), ProtocolJsonContext.Default.DisplayNamePayload));
    }

    public bool TryAnnounce(string[] addresses)
    {
        if (!Trusted)
        {
            return false;
        }

        frames.Add(Json(FrameTypes.PathAnnounce, 1, FrameTypes.SessionStream, new PathAnnouncePayload(addresses), ProtocolJsonContext.Default.PathAnnouncePayload));
        return true;
    }

    void BecomeSurvivor()
    {
        IsSurvivor = true;
        ReadyAt = 0;
        ShortCode = DeviceIds.ShortCode(localSpki, remoteSpki);
    }

    void BecomeDuplicate()
    {
        Closed = true;
        IsSurvivor = false;
        ShortCode = null;
        frames.Add(Json(FrameTypes.SessionDuplicateClose, 0, FrameTypes.SessionStream, new DonePayload(true), ProtocolJsonContext.Default.DonePayload));
    }

    void TryStoreTrust()
    {
        if (!LocalAccepted || !RemoteAccepted || Trust.Count > 0)
        {
            return;
        }

        Trusted = true;
        Trust.Add(new TrustEntry(
            DeviceIds.Text(remoteSpki),
            RemoteName,
            Convert.ToBase64String(remoteSpki),
            ClipboardSync: false,
            AskBeforeSave: true));
        if (CurrentName != HelloName)
        {
            frames.Add(Json(FrameTypes.DisplayName, 0, FrameTypes.SessionStream, new DisplayNamePayload(CurrentName), ProtocolJsonContext.Default.DisplayNamePayload));
        }
    }

    static Frame Json<T>(byte type, byte priority, uint streamId, T payload, JsonTypeInfo<T> info)
        where T : class
    {
        var json = ControlJson.Encode(payload, info);
        var body = json.Value ?? [];
        var encoded = FrameCodec.Encode(body, type, priority, 0, streamId);
        if (encoded.Value is null)
        {
            return new Frame(body.Length, type, priority, 0, streamId, body);
        }

        var decoded = FrameCodec.Decode(encoded.Value);
        if (decoded.Value is Frame encodedFrame)
        {
            return encodedFrame;
        }

        return new Frame(body.Length, type, priority, 0, streamId, body);
    }
}
