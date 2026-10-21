using System.Numerics;

namespace ContextAll.Core;

public static class Base32
{
    const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data) => EncodeBits(data, data.Length * 8);

    public static string EncodeBits(ReadOnlySpan<byte> data, int bits)
    {
        if (bits == 0)
        {
            return "";
        }

        var chars = (bits + 4) / 5;
        var number = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var total = data.Length * 8;
        if (total > bits)
        {
            number >>= total - bits;
        }

        number <<= chars * 5 - bits;
        var output = new char[chars];
        for (var i = chars - 1; i >= 0; i--)
        {
            output[i] = Alphabet[(int)(number & 31)];
            number >>= 5;
        }

        return new string(output);
    }

    public static byte[] DecodeBits(string text, int bits)
    {
        var number = System.Numerics.BigInteger.Zero;
        foreach (var ch in text)
        {
            var index = Alphabet.IndexOf(ch);
            if (index < 0)
            {
                throw new FormatException("base32");
            }

            number = (number << 5) | index;
        }

        var pad = text.Length * 5 - bits;
        if (pad > 0)
        {
            number >>= pad;
        }

        var bytes = (bits + 7) / 8;
        var raw = number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == bytes)
        {
            return raw;
        }

        var full = new byte[bytes];
        raw.CopyTo(full.AsSpan(bytes - raw.Length));
        return full;
    }

    public static byte[] DigestFromDeviceId(string deviceId) =>
        DecodeBits(deviceId[3..], 128);
}
