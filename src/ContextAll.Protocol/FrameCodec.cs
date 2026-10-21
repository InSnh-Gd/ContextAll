using System.Buffers.Binary;

namespace ContextAll.Protocol;

public readonly record struct FrameRead(bool NeedMore, Frame? Frame, int Consumed, string? Reason)
{
    public bool IsFailure => Reason is not null;

    public static FrameRead More() => new(true, null, 0, null);

    public static FrameRead Found(Frame frame, int consumed) => new(false, frame, consumed, null);

    public static FrameRead Bad(string reason) => new(false, null, 0, reason);
}

public static class FrameCodec
{
    public static FrameOutcome<byte[]> Encode(
        ReadOnlyMemory<byte> payload,
        byte type,
        byte priority,
        ushort flags,
        uint streamId,
        uint reserved = 0)
    {
        if (priority > FrameTypes.LowestPriority)
        {
            return FrameOutcome<byte[]>.Failure("priority");
        }

        if (reserved != 0)
        {
            return FrameOutcome<byte[]>.Failure("reserved");
        }

        if (payload.Length > FrameLimits.MaxPayloadBytes)
        {
            return FrameOutcome<byte[]>.Failure("payload-length");
        }

        var bytes = new byte[FrameLimits.HeaderBytes + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)payload.Length);
        bytes[4] = type;
        bytes[5] = priority;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), streamId);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 0);
        payload.Span.CopyTo(bytes.AsSpan(FrameLimits.HeaderBytes));
        return FrameOutcome<byte[]>.Success(bytes);
    }

    public static FrameRead Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FrameLimits.HeaderBytes)
        {
            return FrameRead.More();
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (length > FrameLimits.MaxPayloadBytes)
        {
            return FrameRead.Bad("payload-length");
        }

        var priority = bytes[5];
        if (priority > FrameTypes.LowestPriority)
        {
            return FrameRead.Bad("priority");
        }

        var reserved = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(12));
        if (reserved != 0)
        {
            return FrameRead.Bad("reserved");
        }

        var total = FrameLimits.HeaderBytes + (int)length;
        if (bytes.Length < total)
        {
            return FrameRead.More();
        }

        var payload = bytes.Slice(FrameLimits.HeaderBytes, (int)length).ToArray();
        var frame = new Frame(
            (int)length,
            bytes[4],
            priority,
            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(6)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8)),
            payload);
        return FrameRead.Found(frame, total);
    }

    public static FrameOutcome<Frame> Decode(ReadOnlySpan<byte> bytes)
    {
        var read = Read(bytes);
        if (read.IsFailure)
        {
            return FrameOutcome<Frame>.Failure(read.Reason ?? "malformed");
        }

        if (read.NeedMore || read.Frame is null)
        {
            return FrameOutcome<Frame>.Failure("truncated");
        }

        return FrameOutcome<Frame>.Success(read.Frame);
    }
}
