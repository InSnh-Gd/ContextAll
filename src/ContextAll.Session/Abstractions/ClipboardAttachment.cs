using System.Buffers.Binary;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Session.Abstractions;

public sealed record ClipboardAttachment(string Mime, string Name, byte[] Bytes)
{
    public const string Image = "image/png";
    public const string FileType = "application/octet-stream";

    public static bool IsValid(string? mime, string? name, int length) =>
        (mime is Image or FileType) && length is > 0 and <= FrameLimits.MaxClipboardAttachmentBytes &&
        name is { Length: > 0 and <= 128 } && name != "." && name != ".." &&
        name[0] != '.' && name[^1] is not (' ' or '.') && !name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)) &&
        !IsReserved(name.Split('.')[0]);

    public bool IsValid() => IsValid(Mime, Name, Bytes.Length) && (Mime != Image ||
        Bytes.Length >= 33 && Bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
        Bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) &&
        BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(16, 4)) is > 0 and <= 8192 &&
        BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(20, 4)) is > 0 and <= 8192);

    public static ClipboardAttachment? ReadFile(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
                (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                new FileInfo(path).Length is < 1 or > FrameLimits.MaxClipboardAttachmentBytes) return null;
            using var file = File.OpenRead(path);
            if (file.Length is < 1 or > FrameLimits.MaxClipboardAttachmentBytes || !IsValid(FileType, name, (int)file.Length)) return null;
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            return new ClipboardAttachment(FileType, name, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public string PublishFile(string directory)
    {
        if (!IsValid()) throw new InvalidDataException("invalid-clipboard-attachment");
        var digest = Convert.ToHexString(PlatformCrypto.Sha256(Bytes));
        var folder = Path.Combine(directory, digest);
        Directory.CreateDirectory(folder);
        IdentityStore.SetDirectoryMode(directory);
        IdentityStore.SetDirectoryMode(folder);
        var path = Path.Combine(folder, Name);
        if (!File.Exists(path))
        {
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, Bytes);
            IdentityStore.SetFileMode(temporary);
            File.Move(temporary, path, overwrite: true);
        }

        return path;
    }

    static bool IsReserved(string stem) => stem.ToUpperInvariant() switch
    {
        "CON" or "PRN" or "AUX" or "NUL" => true,
        var value when value.Length == 4 && value[3] is >= '0' and <= '9' =>
            value.StartsWith("COM", StringComparison.Ordinal) || value.StartsWith("LPT", StringComparison.Ordinal),
        _ => false,
    };
}
