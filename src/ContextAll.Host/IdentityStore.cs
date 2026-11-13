using System.Security.Cryptography;
using ContextAll.Core;

namespace ContextAll.Host;

public sealed record IdentityMaterial(string Pem, byte[] Spki, string DeviceId);

public sealed record IdentityStart(bool Ok, IdentityMaterial? Material)
{
    public static IdentityStart Ready(IdentityMaterial material) => new(true, material);

    public static IdentityStart Refuse() => new(false, null);
}

public static class IdentityStore
{
    public static IdentityStart Start(string directory)
    {
        Directory.CreateDirectory(directory);
        SetDirectoryMode(directory);
        var keyPath = Path.Combine(directory, "device.key");
        var spkiPath = Path.Combine(directory, "device.spki");
        var keyExists = File.Exists(keyPath);
        var spkiExists = File.Exists(spkiPath);
        if (keyExists != spkiExists || (keyExists && !Matches(keyPath, spkiPath)))
        {
            return IdentityStart.Refuse();
        }

        if (!keyExists)
        {
            Create(keyPath, spkiPath);
        }

        return IdentityStart.Ready(Read(keyPath, spkiPath));
    }

    public static IdentityMaterial Replace(string directory)
    {
        Directory.CreateDirectory(directory);
        SetDirectoryMode(directory);
        var keyPath = Path.Combine(directory, "device.key");
        var spkiPath = Path.Combine(directory, "device.spki");
        Create(keyPath, spkiPath);
        return Read(keyPath, spkiPath);
    }

    static void Create(string keyPath, string spkiPath)
    {
        var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (created is null)
        {
            throw new InvalidOperationException("P-256 is unavailable.");
        }

        using var key = created;
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllBytes(spkiPath, key.ExportSubjectPublicKeyInfo());
        SetFileMode(keyPath);
        SetFileMode(spkiPath);
    }

    static bool Matches(string keyPath, string spkiPath)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(File.ReadAllText(keyPath));
            return key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(File.ReadAllBytes(spkiPath));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    static IdentityMaterial Read(string keyPath, string spkiPath)
    {
        var pem = File.ReadAllText(keyPath);
        var spki = File.ReadAllBytes(spkiPath);
        return new IdentityMaterial(pem, spki, DeviceIds.Text(spki));
    }

    public static void SetDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public static void SetFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
