using YamlDotNet.Serialization;

namespace ContextAll.Host;

public sealed class TrustDocument
{
    public string DeviceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Spki { get; set; } = "";

    public bool ClipboardSync { get; set; }

    public bool AskBeforeSave { get; set; } = true;
}

public sealed class SettingsDocument
{
    public string DisplayName { get; set; } = "ContextAll";

    public string DownloadDirectory { get; set; } = "";

    public List<TrustDocument> Trust { get; set; } = [];
}

[YamlStaticContext]
[YamlSerializable(typeof(SettingsDocument))]
[YamlSerializable(typeof(TrustDocument))]
public sealed partial class SettingsYamlContext : StaticContext;

public sealed record SettingsStart(bool Ok, SettingsDocument? Document, bool Created);

public static class SettingsStore
{
    static readonly object Gate = new();

    public static SettingsStart Start(string path, string displayName)
    {
        lock (Gate)
        {
            return StartUnlocked(path, displayName);
        }
    }

    static SettingsStart StartUnlocked(string path, string displayName)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            var created = new SettingsDocument
            {
                DisplayName = displayName,
                DownloadDirectory = "",
                Trust = [],
            };
            Write(path, created);
            return new SettingsStart(true, created, true);
        }

        var before = File.ReadAllBytes(path);
        try
        {
            var deserializer = new StaticDeserializerBuilder(new SettingsYamlContext()).Build();
            var document = deserializer.Deserialize<SettingsDocument>(File.ReadAllText(path));
            if (document is null)
            {
                File.WriteAllBytes(path, before);
                return new SettingsStart(false, null, false);
            }

            document.Trust ??= [];
            return new SettingsStart(true, document, false);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            File.WriteAllBytes(path, before);
            return new SettingsStart(false, null, false);
        }
    }

    public static void Write(string path, SettingsDocument document)
    {
        lock (Gate)
        {
            WriteUnlocked(path, document);
        }
    }

    static void WriteUnlocked(string path, SettingsDocument document)
    {
        var serializer = new StaticSerializerBuilder(new SettingsYamlContext()).Build();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, serializer.Serialize(document));
        File.Move(temp, path, overwrite: true);
        IdentityStore.SetFileMode(path);
    }

    public static string ResolveDownload(string stored, string home) =>
        string.IsNullOrEmpty(stored) ? LinuxPaths.Downloads(home) : stored;
}
