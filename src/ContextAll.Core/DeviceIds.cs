using System.Security.Cryptography;

namespace ContextAll.Core;

public static class DeviceIds
{
    public static byte[] Digest(ReadOnlySpan<byte> spki)
    {
        var hash = SHA256.HashData(spki);
        return hash.AsSpan(0, 16).ToArray();
    }

    public static string Text(ReadOnlySpan<byte> spki) => "ca1" + Base32.Encode(Digest(spki));

    public static int Compare(ReadOnlySpan<byte> leftDigest, ReadOnlySpan<byte> rightDigest)
    {
        var length = Math.Min(leftDigest.Length, rightDigest.Length);
        for (var i = 0; i < length; i++)
        {
            var order = leftDigest[i].CompareTo(rightDigest[i]);
            if (order != 0)
            {
                return order;
            }
        }

        return leftDigest.Length.CompareTo(rightDigest.Length);
    }

    public static string ShortCode(ReadOnlySpan<byte> spkiA, ReadOnlySpan<byte> spkiB)
    {
        var first = Compare(Digest(spkiA), Digest(spkiB)) <= 0 ? spkiA : spkiB;
        var second = first.SequenceEqual(spkiA) ? spkiB : spkiA;
        var joined = new byte[first.Length + second.Length];
        first.CopyTo(joined);
        second.CopyTo(joined.AsSpan(first.Length));
        return Base32.EncodeBits(SHA256.HashData(joined), 30);
    }
}
