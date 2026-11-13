using System.Buffers.Binary;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Harness;

public static class FrameStreamReader
{
    public static async Task<FrameOutcome<Frame>> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[FrameLimits.HeaderBytes];
        var readHeader = await ReadExactAsync(stream, header, ct).ConfigureAwait(false);
        if (!readHeader)
        {
            return FrameOutcome<Frame>.Failure("stream-closed-reading-header");
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (length > FrameLimits.MaxPayloadBytes)
        {
            return FrameOutcome<Frame>.Failure($"payload-too-large: {length}");
        }

        var total = FrameLimits.HeaderBytes + (int)length;
        var fullBytes = new byte[total];
        header.CopyTo(fullBytes, 0);

        if (length > 0)
        {
            var readPayload = await ReadExactAsync(stream, fullBytes.AsMemory(FrameLimits.HeaderBytes, (int)length), ct).ConfigureAwait(false);
            if (!readPayload)
            {
                return FrameOutcome<Frame>.Failure("stream-closed-reading-payload");
            }
        }

        var readResult = FrameCodec.Read(fullBytes);
        if (readResult.IsFailure || readResult.Frame is null)
        {
            return FrameOutcome<Frame>.Failure("frame-decode-failed: " + readResult.Reason);
        }

        return FrameOutcome<Frame>.Success(readResult.Frame);
    }

    static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (totalRead == 0) return false;
                throw new EndOfStreamException($"Stream closed prematurely after reading {totalRead} of {buffer.Length} bytes.");
            }
            totalRead += read;
        }
        return true;
    }
}
