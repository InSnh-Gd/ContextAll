using System.Security.Cryptography;
using System.Text;
using ContextAll.Protocol;

namespace ContextAll.Core;

public static class DownloadNames
{
    static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    public static string Sanitize(string original)
    {
        var segment = original.Split('/', '\\')[^1];
        var kept = new StringBuilder();
        var count = 0;
        foreach (var rune in segment.EnumerateRunes())
        {
            if (rune.Value < 0x20 || "\"::*?<>|".Contains(rune.ToString()))
            {
                continue;
            }

            kept.Append(rune.ToString());
            count++;
            if (count == 200)
            {
                break;
            }
        }

        var name = kept.ToString();
        if (name.Length == 0 || name == "." || name == ".." || IsReserved(name))
        {
            return "download";
        }

        return name;
    }

    public static string AvoidCollision(string name, Func<string, bool> exists)
    {
        if (!exists(name))
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var extension = dot > 0 ? name[dot..] : "";
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){extension}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    static bool IsReserved(string name)
    {
        var stem = name.Split('.')[0];
        return Reserved.Contains(name, StringComparer.OrdinalIgnoreCase)
            || Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class TransferSlot
{
    public required string TransferId { get; init; }

    public required string PeerId { get; init; }

    public required string Direction { get; init; }

    public required string FileName { get; init; }

    public required long TotalSize { get; init; }

    public required int SegmentSize { get; init; }

    public required long SegmentCount { get; init; }

    public required string RootSha256 { get; init; }

    public uint StreamId { get; set; }

    public bool WaitingForAccept { get; set; }

    public bool MoveBlocked { get; set; }

    public long Completed { get; set; }

    public byte[] Bits { get; set; } = [];

    public HashSet<long> Flying { get; } = [];

    public bool BitmapComplete { get; set; }

    public long PromptStartedAt { get; set; }

    public HashSet<long> BitmapOffsets { get; } = [];

    public bool Accepted { get; set; }
}

public sealed record TransferWork(string Name, string TransferId, long Offset, byte[]? Data);

public sealed class TransferBook
{
    public const int SegmentSize = FrameLimits.MaxPayloadBytes - 36;
    public const long MaxNumber = 9_007_199_254_740_991;
    readonly Dictionary<string, TransferSlot> outbound = [];
    readonly Dictionary<string, TransferSlot> inbound = [];
    readonly HashSet<uint> retired = [];
    readonly List<TransferWork> works = [];
    readonly List<Frame> frames = [];
    readonly List<(string Peer, string Outcome)> outcomes = [];
    uint nextStream = FrameTypes.FirstBulkStream;
    string? heldPeer;
    string? heldName;
    byte[]? heldBytes;
    bool announcedDone;
    bool peerDone;

    public IReadOnlyList<TransferWork> Works => works;

    public IReadOnlyList<Frame> Frames => frames;

    public void ClearFrames() => frames.Clear();

    public IReadOnlyList<(string Peer, string Outcome)> Outcomes => outcomes;

    public bool AnnouncedDone => announcedDone;

    public TransferSlot? Outbound(string peerId) => outbound.GetValueOrDefault(peerId);

    public TransferSlot? Inbound(string peerId) => inbound.GetValueOrDefault(peerId);

    public string? Offer(string peerId, string fileName, byte[] content, bool sessionUp)
    {
        works.Clear();
        frames.Clear();
        if (!announcedDone)
        {
            if (heldPeer is not null)
            {
                return "busy";
            }

            heldPeer = peerId;
            heldName = fileName;
            heldBytes = content;
            return "held";
        }

        if (outbound.ContainsKey(peerId))
        {
            return "busy";
        }

        if (!sessionUp)
        {
            return "held";
        }

        StartOutbound(peerId, fileName, content);
        return null;
    }

    public void FinishAnnouncement()
    {
        announcedDone = true;
        if (heldPeer is null || heldName is null || heldBytes is null)
        {
            return;
        }

        var peer = heldPeer;
        var name = heldName;
        var bytes = heldBytes;
        heldPeer = null;
        heldName = null;
        heldBytes = null;
        if (outbound.ContainsKey(peer))
        {
            outcomes.Add((peer, "busy"));
            return;
        }

        StartOutbound(peer, name, bytes);
    }

    public void Ready()
    {
        announcedDone = true;
        peerDone = true;
    }

    public void BecomeTrusted(string peerId, IReadOnlyList<TransferSlot> incompleteOutbound)
    {
        works.Clear();
        frames.Clear();
        foreach (var slot in incompleteOutbound)
        {
            slot.StreamId = nextStream++;
            outbound[peerId] = slot;
            frames.Add(Payload(FrameTypes.FileResume, 2, FrameTypes.SessionStream, new FileResumePayload(slot.TransferId, slot.StreamId)));
        }

        frames.Add(Payload(FrameTypes.FileResume, 2, FrameTypes.SessionStream, new DonePayload(true)));
        announcedDone = true;
    }

    public string? ReceiveOffer(FileOfferPayload offer, string peerId, bool askBeforeSave, long nowMs)
    {
        works.Clear();
        frames.Clear();
        if (!Valid(offer) || retired.Contains(offer.StreamId))
        {
            retired.Add(offer.StreamId);
            frames.Add(Reject(offer.TransferId, "invalid"));
            return "invalid";
        }

        if (!peerDone || inbound.ContainsKey(peerId))
        {
            frames.Add(Reject(offer.TransferId, "busy"));
            return "busy";
        }

        var slot = NewSlot(offer, peerId, "receive");
        slot.StreamId = offer.StreamId;
        inbound[peerId] = slot;
        if (askBeforeSave)
        {
            slot.WaitingForAccept = true;
            slot.PromptStartedAt = nowMs;
            return null;
        }

        AcceptSlot(slot);
        return null;
    }

    public string? Accept(string transferId, long nowMs)
    {
        var slot = inbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        if (slot is null || !slot.WaitingForAccept)
        {
            return "already-decided";
        }

        if (nowMs - slot.PromptStartedAt >= 60_000)
        {
            return Timeout(slot);
        }

        slot.WaitingForAccept = false;
        AcceptSlot(slot);
        return null;
    }

    public string? RejectPrompt(string transferId)
    {
        var slot = inbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        if (slot is null || !slot.WaitingForAccept)
        {
            return "already-decided";
        }

        frames.Add(Reject(slot.TransferId, "declined"));
        inbound.Remove(slot.PeerId);
        return null;
    }

    public string? Timeout(string peerId, long nowMs)
    {
        if (inbound.TryGetValue(peerId, out var slot) && slot.WaitingForAccept && nowMs - slot.PromptStartedAt >= 60_000)
        {
            return Timeout(slot);
        }

        return null;
    }

    public void ReceiveBitmap(string transferId, long bitOffset, byte[] bits, bool last)
    {
        var slot = outbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        if (slot is null)
        {
            return;
        }

        slot.BitmapOffsets.Add(bitOffset);
        if (last)
        {
            slot.BitmapComplete = Covers(slot);
            slot.Bits = bits;
        }

        if (slot.BitmapComplete)
        {
            Pump(slot);
        }
    }

    public void Ack(string transferId, long index)
    {
        var slot = outbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        if (slot is null)
        {
            return;
        }

        slot.Flying.Remove(index);
        SetBit(slot, (uint)index);
        frames.Clear();
        Pump(slot);
    }

    public void Release(string peerId)
    {
        if (outbound.TryGetValue(peerId, out var slot))
        {
            frames.Clear();
            Pump(slot);
        }
    }

    public TransferSlot SeedOutbound(string peerId, long segmentCount, params long[] stored)
    {
        Ready();
        var slot = new TransferSlot
        {
            TransferId = new string('a', 32),
            PeerId = peerId,
            Direction = "send",
            FileName = "file.bin",
            TotalSize = segmentCount * SegmentSize,
            SegmentSize = SegmentSize,
            SegmentCount = segmentCount,
            RootSha256 = new string('b', 64),
            StreamId = nextStream++,
            Accepted = true,
            BitmapComplete = true,
            Bits = new byte[(segmentCount + 7) / 8],
        };
        foreach (var index in stored)
        {
            SetBit(slot, (uint)index);
        }

        outbound[peerId] = slot;
        return slot;
    }

    public void ReceiveSegment(string peerId, uint index, byte[] hash, byte[] data, byte[]? runningFile)
    {
        works.Clear();
        if (!inbound.TryGetValue(peerId, out var slot) || !slot.Accepted)
        {
            return;
        }

        var expected = SHA256.HashData(data);
        if (!expected.AsSpan().SequenceEqual(hash))
        {
            outcomes.Add((peerId, "checksum-failed"));
            works.Add(new TransferWork("delete", slot.TransferId, 0, null));
            inbound.Remove(peerId);
            return;
        }

        var offset = (long)index * slot.SegmentSize;
        works.Add(new TransferWork("write", slot.TransferId, offset, data));
        works.Add(new TransferWork("flush", slot.TransferId, offset, null));
        SetBit(slot, index);
        works.Add(new TransferWork("bit", slot.TransferId, index, null));
        frames.Add(Payload(FrameTypes.FileAck, 2, FrameTypes.SessionStream, new FileAckPayload(slot.TransferId, index)));
        if (slot.Completed == slot.SegmentCount)
        {
            Finish(slot, runningFile);
        }
    }

    public void Cancel(string transferId, bool sessionUp)
    {
        works.Clear();
        var inboundSlot = inbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        var outboundSlot = outbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        var slot = inboundSlot ?? outboundSlot;
        if (slot is null)
        {
            return;
        }

        if (sessionUp)
        {
            frames.Add(Payload(FrameTypes.FileCancel, 0, FrameTypes.SessionStream, new FileCancelPayload(transferId)));
        }

        works.Add(new TransferWork("delete", transferId, 0, null));
        if (inboundSlot is not null)
        {
            inbound.Remove(inboundSlot.PeerId);
        }
        else if (outboundSlot is not null)
        {
            outbound.Remove(outboundSlot.PeerId);
        }
    }

    public void PeerDone(string peerId, IReadOnlyCollection<string> announcedIds)
    {
        peerDone = true;
        var slot = inbound.GetValueOrDefault(peerId);
        if (slot is null)
        {
            return;
        }

        if (!announcedIds.Contains(slot.TransferId))
        {
            works.Add(new TransferWork("delete", slot.TransferId, 0, null));
            outcomes.Add((peerId, "gone"));
            inbound.Remove(peerId);
        }
    }

    public string? ResumeUnknown(string transferId)
    {
        var slot = outbound.Values.FirstOrDefault(item => item.TransferId == transferId);
        frames.Add(Reject(transferId, "gone"));
        if (slot is not null)
        {
            works.Add(new TransferWork("delete", transferId, 0, null));
            outbound.Remove(slot.PeerId);
        }

        return "gone";
    }

    public void SendClearBits(TransferSlot slot, byte[] file)
    {
        slot.BitmapComplete = true;
        slot.Bits = BitsFor(slot, file);
        Pump(slot);
    }

    public bool TryMove(TransferSlot slot, bool renameOk, bool copyOk)
    {
        if (renameOk || copyOk)
        {
            works.Add(new TransferWork("delete", slot.TransferId, 0, null));
            inbound.Remove(slot.PeerId);
            outcomes.Add((slot.PeerId, "completed"));
            slot.MoveBlocked = false;
            return true;
        }

        slot.MoveBlocked = true;
        return false;
    }

    void StartOutbound(string peerId, string fileName, byte[] content)
    {
        var count = content.Length == 0 ? 0 : (content.Length + SegmentSize - 1) / SegmentSize;
        var slot = new TransferSlot
        {
            TransferId = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray()))[..32].ToLowerInvariant(),
            PeerId = peerId,
            Direction = "send",
            FileName = fileName,
            TotalSize = content.Length,
            SegmentSize = SegmentSize,
            SegmentCount = count,
            RootSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            StreamId = nextStream++,
            Accepted = true,
            Bits = new byte[(count + 7) / 8],
        };
        outbound[peerId] = slot;
        frames.Add(Payload(FrameTypes.FileOffer, 2, FrameTypes.SessionStream, new FileOfferPayload(
            slot.TransferId,
            slot.StreamId,
            slot.TotalSize,
            slot.SegmentSize,
            slot.SegmentCount,
            slot.RootSha256,
            slot.FileName)));
    }

    void AcceptSlot(TransferSlot slot)
    {
        slot.Accepted = true;
        slot.WaitingForAccept = false;
        works.Add(new TransferWork("create", slot.TransferId, 0, null));
        frames.Add(Payload(FrameTypes.FileAccept, 0, FrameTypes.SessionStream, new FileAcceptPayload(slot.TransferId)));
        frames.Add(BitmapFrame(slot.TransferId, 0, [], last: true));
        if (slot.SegmentCount == 0)
        {
            Finish(slot, []);
        }
    }

    void Pump(TransferSlot slot)
    {
        if (!slot.BitmapComplete)
        {
            return;
        }

        while (slot.Flying.Count < 8)
        {
            var next = NextClear(slot);
            if (next is not long index)
            {
                return;
            }

            slot.Flying.Add(index);
            var payload = new byte[4];
            payload[0] = (byte)((index >> 24) & 0xFF);
            payload[1] = (byte)((index >> 16) & 0xFF);
            payload[2] = (byte)((index >> 8) & 0xFF);
            payload[3] = (byte)(index & 0xFF);
            frames.Add(new Frame(4, FrameTypes.FileSegment, 3, 0, slot.StreamId, payload));
        }
    }

    static long? NextClear(TransferSlot slot)
    {
        for (long index = 0; index < slot.SegmentCount; index++)
        {
            if (!Bit(slot, index) && !slot.Flying.Contains(index))
            {
                return index;
            }
        }

        return null;
    }

    static bool Bit(TransferSlot slot, long index)
    {
        var byteIndex = (int)(index / 8);
        if (byteIndex >= slot.Bits.Length)
        {
            return false;
        }

        return (slot.Bits[byteIndex] & (1 << (int)(index % 8))) != 0;
    }

    static void SetBit(TransferSlot slot, uint index)
    {
        var bits = slot.Bits;
        var byteIndex = (int)(index / 8);
        if (bits.Length <= byteIndex)
        {
            Array.Resize(ref bits, byteIndex + 1);
            slot.Bits = bits;
        }

        if ((slot.Bits[byteIndex] & (1 << (int)(index % 8))) == 0)
        {
            slot.Bits[byteIndex] |= (byte)(1 << (int)(index % 8));
            slot.Completed++;
        }
    }

    static TransferSlot NewSlot(FileOfferPayload offer, string peerId, string direction) =>
        new()
        {
            TransferId = offer.TransferId,
            PeerId = peerId,
            Direction = direction,
            FileName = offer.FileName,
            TotalSize = offer.TotalSize,
            SegmentSize = offer.SegmentSize,
            SegmentCount = offer.SegmentCount,
            RootSha256 = offer.RootSha256,
            Bits = new byte[(Math.Max(offer.SegmentCount, 1) + 7) / 8],
        };

    static bool Covers(TransferSlot slot) => slot.BitmapOffsets.Contains(0);

    static byte[] BitsFor(TransferSlot slot, byte[] file)
    {
        _ = file;
        return slot.Bits;
    }

    void Finish(TransferSlot slot, byte[]? file)
    {
        _ = file;
        outcomes.Add((slot.PeerId, "ready"));
    }

    string Timeout(TransferSlot slot)
    {
        frames.Add(Reject(slot.TransferId, "timeout"));
        outcomes.Add((slot.PeerId, "timeout"));
        inbound.Remove(slot.PeerId);
        return "timeout";
    }

    static bool Valid(FileOfferPayload offer) =>
        offer.TransferId.Length == 32
        && offer.RootSha256.Length == 64
        && offer.TransferId.All(Uri.IsHexDigit)
        && offer.RootSha256.All(Uri.IsHexDigit)
        && offer.SegmentSize == SegmentSize
        && offer.TotalSize >= 0
        && offer.TotalSize <= MaxNumber
        && offer.SegmentCount >= 0
        && offer.SegmentCount <= MaxNumber
        && offer.SegmentCount == (offer.TotalSize == 0 ? 0 : (offer.TotalSize + SegmentSize - 1) / SegmentSize);

    static Frame Reject(string transferId, string reason) =>
        Payload(FrameTypes.FileReject, 0, FrameTypes.SessionStream, new FileRejectPayload(transferId, reason));

    static Frame BitmapFrame(string transferId, long bitOffset, byte[] bits, bool last)
    {
        var json = ControlJson.Encode(
            new FileBitmapPayload(transferId, bitOffset, Convert.ToBase64String(bits)),
            ProtocolJsonContext.Default.FileBitmapPayload);
        var flags = last ? (ushort)0 : FrameTypes.BitmapContinued;
        var encoded = FrameCodec.Encode(json.Value ?? [], FrameTypes.FileBitmap, 2, flags, FrameTypes.SessionStream);
        return FrameCodec.Decode(encoded.Value ?? []).Value
            ?? new Frame(0, FrameTypes.FileBitmap, 2, flags, FrameTypes.SessionStream, json.Value ?? []);
    }

    static Frame Payload<T>(byte type, byte priority, uint streamId, T payload)
        where T : class
    {
        var info = ProtocolJsonContext.Default.GetTypeInfo(typeof(T)) as System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>;
        var json = info is null
            ? FrameOutcome<byte[]>.Failure("json")
            : ControlJson.Encode(payload, info);
        var encoded = FrameCodec.Encode(json.Value ?? [], type, priority, 0, streamId);
        return FrameCodec.Decode(encoded.Value ?? []).Value
            ?? new Frame(0, type, priority, 0, streamId, json.Value ?? []);
    }

    public static IReadOnlyList<Frame> SplitBitmap(string transferId, byte[] bits)
    {
        const int rawBudget = 30_000;
        var frames = new List<Frame>();
        var offset = 0;
        if (bits.Length == 0)
        {
            frames.Add(BitmapFrame(transferId, 0, [], true));
            return frames;
        }

        while (offset < bits.Length)
        {
            var take = Math.Min(rawBudget, bits.Length - offset);
            var last = offset + take >= bits.Length;
            frames.Add(BitmapFrame(transferId, offset * 8, bits.AsSpan(offset, take).ToArray(), last));
            offset += take;
        }

        return frames;
    }
}
