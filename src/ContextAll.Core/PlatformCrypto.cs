using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace ContextAll.Core;

public static class PlatformCrypto
{
    static volatile bool useManagedSha256;

    /// <summary>
    /// Selects the pure-managed SHA-256 implementation so linux-bionic/Android NativeAOT
    /// avoids loading OpenSSL (<c>libcrypto.so</c>) at runtime.
    /// </summary>
    public static bool UseManagedSha256
    {
        get => useManagedSha256;
        set => useManagedSha256 = value;
    }

    public static Action<string>? DiagnosticSink { get; set; }

    public static void LogDiagnostic(string message) => DiagnosticSink?.Invoke(message);

    public static byte[] Sha256(ReadOnlySpan<byte> data) =>
        useManagedSha256 ? ManagedSha256.HashData(data) : SHA256.HashData(data);

    public static byte[] Sha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return useManagedSha256 ? ManagedSha256.HashData(stream) : SHA256.HashData(stream);
    }
}

/// <summary>
/// Pure-managed FIPS 180-4 SHA-256 implementation with zero native OpenSSL dependencies.
/// </summary>
public sealed class ManagedSha256
{
    static ReadOnlySpan<uint> K =>
    [
        0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
        0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
        0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
        0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
        0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
        0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
        0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
        0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u,
    ];

    readonly uint[] state = new uint[8];
    readonly byte[] block = new byte[64];
    int blockOffset;
    ulong totalBytes;

    public ManagedSha256() => Reset();

    public void Reset()
    {
        state[0] = 0x6a09e667u;
        state[1] = 0xbb67ae85u;
        state[2] = 0x3c6ef372u;
        state[3] = 0xa54ff53au;
        state[4] = 0x510e527fu;
        state[5] = 0x9b05688cu;
        state[6] = 0x1f83d9abu;
        state[7] = 0x5be0cd19u;
        blockOffset = 0;
        totalBytes = 0;
    }

    public void AppendData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        totalBytes += (ulong)data.Length;
        Span<uint> schedule = stackalloc uint[64];

        if (blockOffset > 0)
        {
            var needed = 64 - blockOffset;
            if (data.Length < needed)
            {
                data.CopyTo(block.AsSpan(blockOffset));
                blockOffset += data.Length;
                return;
            }

            data[..needed].CopyTo(block.AsSpan(blockOffset));
            CompressBlock(block, schedule);
            blockOffset = 0;
            data = data[needed..];
        }

        while (data.Length >= 64)
        {
            CompressBlock(data[..64], schedule);
            data = data[64..];
        }

        if (!data.IsEmpty)
        {
            data.CopyTo(block);
            blockOffset = data.Length;
        }
    }

    public byte[] GetHashAndReset()
    {
        var hash = new byte[32];
        GetHashAndReset(hash);
        return hash;
    }

    public void GetHashAndReset(Span<byte> destination)
    {
        if (destination.Length < 32)
        {
            throw new ArgumentException("Destination buffer must be at least 32 bytes.", nameof(destination));
        }

        Span<uint> schedule = stackalloc uint[64];
        var bitLength = totalBytes * 8;

        block[blockOffset++] = 0x80;
        if (blockOffset > 56)
        {
            block.AsSpan(blockOffset).Clear();
            CompressBlock(block, schedule);
            blockOffset = 0;
        }

        block.AsSpan(blockOffset, 56 - blockOffset).Clear();
        BinaryPrimitives.WriteUInt64BigEndian(block.AsSpan(56), bitLength);
        CompressBlock(block, schedule);

        for (var i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(i * 4, 4), state[i]);
        }

        Reset();
    }

    public static byte[] HashData(ReadOnlySpan<byte> data)
    {
        var hasher = new ManagedSha256();
        hasher.AppendData(data);
        return hasher.GetHashAndReset();
    }

    public static byte[] HashData(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var hasher = new ManagedSha256();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                hasher.AppendData(buffer.AsSpan(0, read));
            }

            return hasher.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    void CompressBlock(ReadOnlySpan<byte> chunk, Span<uint> w)
    {
        for (var i = 0; i < 16; i++)
        {
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(i * 4, 4));
        }

        for (var i = 16; i < 64; i++)
        {
            var s0 = BitOperations.RotateRight(w[i - 15], 7) ^ BitOperations.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
            var s1 = BitOperations.RotateRight(w[i - 2], 17) ^ BitOperations.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        var a = state[0];
        var b = state[1];
        var c = state[2];
        var d = state[3];
        var e = state[4];
        var f = state[5];
        var g = state[6];
        var h = state[7];

        var k = K;
        for (var i = 0; i < 64; i++)
        {
            var s1 = BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25);
            var ch = (e & f) ^ (~e & g);
            var temp1 = h + s1 + ch + k[i] + w[i];
            var s0 = BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22);
            var maj = (a & b) ^ (a & c) ^ (b & c);
            var temp2 = s0 + maj;

            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        state[0] += a;
        state[1] += b;
        state[2] += c;
        state[3] += d;
        state[4] += e;
        state[5] += f;
        state[6] += g;
        state[7] += h;
    }
}

