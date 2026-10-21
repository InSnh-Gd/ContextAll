using System.Buffers.Binary;
using System.Text;

namespace ContextAll.Protocol;

public sealed record Beacon(byte[] Digest, ushort Port, string Name);

public static class BeaconCodec
{
    public static FrameOutcome<byte[]> Encode(ReadOnlySpan<byte> digest, ushort port, string name)
    {
        if (digest.Length != 16)
        {
            return FrameOutcome<byte[]>.Failure("digest");
        }

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var total = 25 + nameBytes.Length;
        if (total > ushort.MaxValue)
        {
            return FrameOutcome<byte[]>.Failure("length");
        }

        var bytes = new byte[total];
        bytes[0] = (byte)'C';
        bytes[1] = (byte)'A';
        bytes[2] = (byte)'0';
        bytes[3] = (byte)'1';
        bytes[4] = 1;
        digest.CopyTo(bytes.AsSpan(5));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(21), port);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(23), (ushort)total);
        nameBytes.CopyTo(bytes.AsSpan(25));
        return FrameOutcome<byte[]>.Success(bytes);
    }

    public static FrameOutcome<Beacon> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 25
            || bytes[0] != (byte)'C'
            || bytes[1] != (byte)'A'
            || bytes[2] != (byte)'0'
            || bytes[3] != (byte)'1'
            || bytes[4] != 1)
        {
            return FrameOutcome<Beacon>.Failure("beacon");
        }

        var declared = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(23));
        if (declared != bytes.Length)
        {
            return FrameOutcome<Beacon>.Failure("length");
        }

        return FrameOutcome<Beacon>.Success(new Beacon(
            bytes.Slice(5, 16).ToArray(),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(21)),
            Encoding.UTF8.GetString(bytes.Slice(25))));
    }
}
