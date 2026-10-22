using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;
using ContextAll.Protocol;

namespace ContextAll.Host;

static class FrameMessages
{
    public static Frame Json<T>(byte type, byte priority, ushort flags, uint streamId, T payload, JsonTypeInfo<T> info)
        where T : class
    {
        var json = ControlJson.Encode(payload, info);
        var body = json.Value ?? [];
        return new Frame(body.Length, type, priority, flags, streamId, body);
    }

    public static byte[] Encode(Frame frame)
    {
        var encoded = FrameCodec.Encode(frame.Payload, frame.Type, frame.Priority, frame.Flags, frame.StreamId);
        return encoded.Value ?? [];
    }

    public static Frame Segment(uint streamId, long index, ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        var payload = new byte[36 + data.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)index);
        hash.CopyTo(payload.AsSpan(4));
        data.CopyTo(payload.AsSpan(36));
        return new Frame(payload.Length, FrameTypes.FileSegment, 3, 0, streamId, payload);
    }

    public static uint BulkAfter(IEnumerable<Frame> frames, uint current)
    {
        var next = current;
        foreach (var frame in frames)
        {
            if (frame.StreamId >= FrameTypes.FirstBulkStream && frame.StreamId + 1 > next)
            {
                next = frame.StreamId + 1;
            }
        }

        return next;
    }
}
