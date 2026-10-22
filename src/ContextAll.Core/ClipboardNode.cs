using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using ContextAll.Protocol;

namespace ContextAll.Core;

public readonly record struct HybridClock(long Milliseconds, uint Counter, string Origin)
{
    public static int Compare(HybridClock left, HybridClock right)
    {
        var milliseconds = left.Milliseconds.CompareTo(right.Milliseconds);
        if (milliseconds != 0)
        {
            return milliseconds;
        }

        var counter = left.Counter.CompareTo(right.Counter);
        if (counter != 0)
        {
            return counter;
        }

        var leftDigest = Base32.DigestFromDeviceId(left.Origin);
        var rightDigest = Base32.DigestFromDeviceId(right.Origin);
        return DeviceIds.Compare(leftDigest, rightDigest);
    }
}

public static class HybridClocks
{
    public static HybridClock Issue(HybridClock? last, long physicalMs, string self)
    {
        if (last is null)
        {
            return new HybridClock(physicalMs, 0, self);
        }

        var milliseconds = Math.Max(physicalMs, last.Value.Milliseconds);
        var counter = milliseconds == last.Value.Milliseconds ? last.Value.Counter + 1 : 0;
        var next = new HybridClock(milliseconds, counter, self);
        if (HybridClock.Compare(next, last.Value) <= 0)
        {
            next = new HybridClock(milliseconds, last.Value.Counter + 1, self);
        }

        return next;
    }
}

public enum ClipboardKind
{
    Plain,
    Image,
    FileList,
}

public sealed record DirectedFrame(string PeerId, Frame Frame);

public sealed record ClipboardDocument(
    string Text,
    HybridClock? Clock,
    Dictionary<string, long> Vector,
    HybridClock? Last,
    string AppliedText,
    long Sequence);

public sealed record RestartPlan(
    ClipboardDocument Document,
    bool WriteClipboard,
    string? ClipboardText,
    bool NewLocalChange,
    bool Listening,
    int WindowCount);

public static class ClipboardRestart
{
    public const int MaxBytes = 1024 * 1024;

    public static RestartPlan Plan(ClipboardDocument? stored, bool missingOrUnreadable, string? plain, bool isPlain)
    {
        if (missingOrUnreadable || stored is null)
        {
            var text = isPlain && plain is not null && Encoding.UTF8.GetByteCount(plain) <= MaxBytes ? plain : "";
            var created = new ClipboardDocument(text, null, [], null, text, 0);
            return new RestartPlan(created, false, null, false, true, 0);
        }

        if (!isPlain || plain is null)
        {
            return new RestartPlan(stored, false, null, false, true, 0);
        }

        if (plain == stored.Text)
        {
            return new RestartPlan(stored with { AppliedText = stored.Text }, false, null, false, true, 0);
        }

        if (plain == stored.AppliedText)
        {
            return new RestartPlan(stored, true, stored.Text, false, true, 0);
        }

        var sequence = stored.Sequence + 1;
        var vector = new Dictionary<string, long>(stored.Vector);
        return new RestartPlan(
            stored with { Text = plain, AppliedText = plain, Sequence = sequence, Vector = vector },
            false,
            null,
            true,
            true,
            0);
    }
}

public sealed class ClipboardNode
{
    readonly Dictionary<string, (bool Local, bool Remote)> peers = [];
    readonly Queue<(string Origin, long Seq)> window = new();
    readonly Dictionary<uint, Assembly> assemblies = [];
    readonly List<DirectedFrame> lastSent = [];
    uint nextStream = FrameTypes.FirstBulkStream;

    public ClipboardNode(string deviceId)
    {
        DeviceId = deviceId;
    }

    public string DeviceId { get; }

    public string Text { get; private set; } = "";

    public HybridClock? Clock { get; private set; }

    public HybridClock? Last { get; private set; }

    public long Sequence { get; private set; }

    public int Notifications { get; private set; }

    public int ApplyCount { get; private set; }

    public Dictionary<string, long> Vector { get; } = [];

    public int WindowCount => window.Count;

    public uint NextBulk
    {
        get => nextStream;
        set
        {
            if (value > nextStream)
            {
                nextStream = value;
            }
        }
    }

    public void Restore(ClipboardDocument document)
    {
        Text = document.Text;
        Clock = document.Clock;
        Last = document.Last;
        Sequence = document.Sequence;
        Vector.Clear();
        foreach (var pair in document.Vector)
        {
            Vector[pair.Key] = pair.Value;
        }
    }

    public ClipboardDocument ToDocument(string appliedText) =>
        new(Text, Clock, new Dictionary<string, long>(Vector), Last, appliedText, Sequence);

    public IReadOnlyList<DirectedFrame> LastSent => lastSent;

    public void Connect(string peerId, bool localEnabled, bool remoteEnabled) =>
        peers[peerId] = (localEnabled, remoteEnabled);

    public void ObserveRemoteClock(HybridClock remote) =>
        Last = HybridClocks.Issue(Last, remote.Milliseconds, DeviceId) is var issued
            && HybridClock.Compare(issued, remote) > 0
            ? issued
            : HybridClocks.Issue(remote, remote.Milliseconds, DeviceId);

    public IReadOnlyList<DirectedFrame> OnLocal(ClipboardKind kind, string text, long physicalMs)
    {
        lastSent.Clear();
        if (kind != ClipboardKind.Plain)
        {
            return lastSent;
        }

        if (Encoding.UTF8.GetByteCount(text) > ClipboardRestart.MaxBytes)
        {
            return lastSent;
        }

        Sequence++;
        var clock = HybridClocks.Issue(Last, physicalMs, DeviceId);
        Last = clock;
        Clock = clock;
        Text = text;
        Vector[DeviceId] = Sequence;
        foreach (var (peerId, opt) in peers)
        {
            if (!opt.Local || !opt.Remote)
            {
                continue;
            }

            foreach (var frame in EncodeChange(clock, Sequence, text))
            {
                lastSent.Add(new DirectedFrame(peerId, frame));
            }
        }

        return lastSent;
    }

    public IReadOnlyList<Frame> OnSyncOpen(string peerId, Dictionary<string, long> remoteVector, HybridClock? remoteClock, bool mutual)
    {
        var frames = new List<Frame>
        {
            VectorFrame(),
        };
        if (!mutual)
        {
            return frames;
        }

        if (Clock is HybridClock ours && (remoteClock is null || HybridClock.Compare(ours, remoteClock.Value) > 0))
        {
            frames.AddRange(EncodeChange(ours, Sequence, Text));
        }

        _ = remoteVector;
        return frames;
    }

    public IReadOnlyList<DirectedFrame> ReceiveDelta(string fromPeer, DeltaPayload delta, bool mutual)
    {
        lastSent.Clear();
        if (window.Any(entry => entry.Origin == delta.Origin && entry.Seq == delta.Seq))
        {
            return lastSent;
        }

        Remember(delta.Origin, delta.Seq);
        var clock = new HybridClock(delta.Milliseconds, delta.Counter, delta.Origin);
        if (delta.Text is null)
        {
            if (delta.StreamId is uint streamId && delta.ByteCount is long count && delta.Sha256 is string hash)
            {
                if (count <= 0 || count > ClipboardRestart.MaxBytes)
                {
                    return lastSent;
                }

                DiscardOlder(clock);
                assemblies[streamId] = new Assembly(clock, delta.Seq, count, hash, new byte[(int)count]);
            }

            return lastSent;
        }

        if (!mutual)
        {
            return lastSent;
        }

        ApplyIfNewer(clock, delta.Text, delta.Seq, delta.Origin);
        return lastSent;
    }

    public void ReceiveChunk(uint streamId, ReadOnlySpan<byte> payload, bool mutual)
    {
        if (!assemblies.TryGetValue(streamId, out var assembly))
        {
            return;
        }

        if (payload.Length < 4)
        {
            assemblies.Remove(streamId);
            return;
        }

        var offset = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
        var data = payload[4..];
        if (offset < 0 || offset + data.Length > assembly.Buffer.Length)
        {
            assemblies.Remove(streamId);
            return;
        }

        data.CopyTo(assembly.Buffer.AsSpan(offset));
        assembly.Filled += data.Length;
        if (assembly.Filled < assembly.Buffer.Length)
        {
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(assembly.Buffer)).ToLowerInvariant();
        assemblies.Remove(streamId);
        if (!mutual || hash != assembly.Hash)
        {
            return;
        }

        var text = Encoding.UTF8.GetString(assembly.Buffer);
        if (Encoding.UTF8.GetByteCount(text) != assembly.Buffer.Length)
        {
            return;
        }

        ApplyIfNewer(assembly.Clock, text, assembly.Seq, assembly.Clock.Origin);
    }

    List<Frame> EncodeChange(HybridClock clock, long sequence, string text)
    {
        var withText = new DeltaPayload(
            DeviceId,
            sequence,
            clock.Milliseconds,
            clock.Counter,
            text,
            null,
            null,
            null);
        var json = ControlJson.Encode(withText, ProtocolJsonContext.Default.DeltaPayload);
        if (json.Value is { Length: <= FrameLimits.MaxPayloadBytes })
        {
            return [SessionFrame(FrameTypes.CrdtDeltaBatch, 1, FrameTypes.SyncStream, json.Value)];
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var streamId = nextStream++;
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var header = new DeltaPayload(
            DeviceId,
            sequence,
            clock.Milliseconds,
            clock.Counter,
            null,
            streamId,
            bytes.Length,
            hash);
        var headerJson = ControlJson.Encode(header, ProtocolJsonContext.Default.DeltaPayload);
        var frames = new List<Frame>
        {
            SessionFrame(FrameTypes.CrdtDeltaBatch, 1, FrameTypes.SyncStream, headerJson.Value ?? []),
        };
        var chunkData = FrameLimits.MaxPayloadBytes - 4;
        for (var offset = 0; offset < bytes.Length; offset += chunkData)
        {
            var take = Math.Min(chunkData, bytes.Length - offset);
            var payload = new byte[4 + take];
            payload[0] = (byte)((offset >> 24) & 0xFF);
            payload[1] = (byte)((offset >> 16) & 0xFF);
            payload[2] = (byte)((offset >> 8) & 0xFF);
            payload[3] = (byte)(offset & 0xFF);
            bytes.AsSpan(offset, take).CopyTo(payload.AsSpan(4));
            frames.Add(SessionFrame(FrameTypes.ClipboardChunk, 3, streamId, payload));
        }

        return frames;
    }

    Frame VectorFrame()
    {
        var payload = new VectorPayload(new Dictionary<string, long>(Vector), Clock is HybridClock clock
            ? new ClockPayload(clock.Milliseconds, clock.Counter, clock.Origin)
            : null);
        var json = ControlJson.Encode(payload, ProtocolJsonContext.Default.VectorPayload);
        return SessionFrame(FrameTypes.CrdtStateVector, 1, FrameTypes.SyncStream, json.Value ?? []);
    }

    void ApplyIfNewer(HybridClock clock, string text, long sequence, string origin)
    {
        if (Vector.TryGetValue(origin, out var seen) && sequence <= seen)
        {
            return;
        }

        if (Clock is HybridClock current && HybridClock.Compare(clock, current) <= 0)
        {
            return;
        }

        DiscardOlder(clock);
        Text = text;
        Clock = clock;
        Last = HybridClocks.Issue(Last, clock.Milliseconds, DeviceId);
        if (HybridClock.Compare(Last.Value, clock) <= 0)
        {
            Last = new HybridClock(clock.Milliseconds, clock.Counter + 1, DeviceId);
        }

        Vector[origin] = Math.Max(Vector.GetValueOrDefault(origin), sequence);
        ApplyCount++;
    }

    void DiscardOlder(HybridClock clock)
    {
        var stale = assemblies
            .Where(pair => HybridClock.Compare(clock, pair.Value.Clock) > 0)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in stale)
        {
            assemblies.Remove(key);
        }
    }

    void Remember(string origin, long sequence)
    {
        window.Enqueue((origin, sequence));
        while (window.Count > 512)
        {
            window.Dequeue();
        }
    }

    static Frame SessionFrame(byte type, byte priority, uint streamId, byte[] payload)
    {
        var encoded = FrameCodec.Encode(payload, type, priority, 0, streamId);
        if (encoded.Value is null)
        {
            return new Frame(payload.Length, type, priority, 0, streamId, payload);
        }

        var decoded = FrameCodec.Decode(encoded.Value);
        if (decoded.Value is Frame frame)
        {
            return frame;
        }

        return new Frame(payload.Length, type, priority, 0, streamId, payload);
    }

    sealed class Assembly(HybridClock clock, long seq, long count, string hash, byte[] buffer)
    {
        public HybridClock Clock { get; } = clock;

        public long Seq { get; } = seq;

        public string Hash { get; } = hash;

        public byte[] Buffer { get; } = buffer;

        public int Filled { get; set; }

        public long Count { get; } = count;
    }
}
