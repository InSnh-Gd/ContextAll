using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Session;

internal sealed class ClipboardHistoryStore(string directory, Func<string, string> sourceName)
{
    const int PageSize = 20;
    const int Capacity = 256;
    readonly string indexPath = Path.Combine(directory, "index.json");
    readonly HistoryIndex index = Load(Path.Combine(directory, "index.json"));
    long nextCleanup;
    public int RetentionDays => index.RetentionDays;

    public string? Configure(bool? enabled, int? days, long now)
    {
        if (days is < 1 or > 30 || (enabled is null && days is null))
        {
            return "invalid";
        }

        index.Enabled = enabled ?? index.Enabled;
        index.RetentionDays = days ?? index.RetentionDays;
        Cleanup(now, force: true);
        Save();
        return null;
    }

    public bool Record(HybridClock clock, long sequence, string text, long now, ClipboardGroupItem? group = null)
        => Record(clock, sequence, text, System.Text.Encoding.UTF8.GetBytes(text), now, group);

    public bool Record(HybridClock clock, long sequence, string title, byte[] bytes, long now, ClipboardGroupItem? group)
    {
        if (!index.Enabled)
        {
            return false;
        }

        var copiedAt = Math.Min(clock.Milliseconds, now);
        if (copiedAt <= Cutoff(now))
        {
            return false;
        }

        var key = Key(clock.Origin, sequence, group?.Group);
        if (index.Entries.Any(entry => entry.Id == key))
        {
            return false;
        }

        EnsureDirectory();
        var preview = title.Length <= 200 ? title : title[..(char.IsHighSurrogate(title[199]) ? 199 : 200)];
        WritePrivate(PathFor(key), bytes);
        index.Entries.Add(new ClipboardHistoryEntry(key, clock.Origin, "", preview, copiedAt));
        if (group is not null)
        {
            index.GroupItems[key] = group;
        }
        Cleanup(now, force: index.Entries.Count > Capacity);
        Save();
        return true;
    }

    public IEnumerable<(ClipboardGroupItem Item, byte[] Bytes)> Replay(string group, long since, string? from, string? except, long now)
    {
        if (!index.Enabled)
        {
            yield break;
        }

        var candidates = index.Entries.Where(entry => entry.CopiedAt > Cutoff(now) &&
                index.GroupItems.TryGetValue(entry.Id, out var item) && item.Group == group && item.Milliseconds >= since &&
                (from is not null ? item.Origin == from : item.RelayedAt >= since) && item.Origin != except)
            .OrderBy(entry => entry.CopiedAt).TakeLast(32);
        foreach (var entry in candidates)
        {
            if (!index.GroupItems.TryGetValue(entry.Id, out var item) || !File.Exists(PathFor(entry.Id)))
            {
                continue;
            }

            yield return (item with { Replay = true }, File.ReadAllBytes(PathFor(entry.Id)));
        }
    }

    public bool Contains(ClipboardGroupItem item) => index.GroupItems.ContainsKey(Key(item.Origin, item.Sequence, item.Group));

    public Abstractions.ClipboardAttachment? ReadAttachment(string? id, long now)
    {
        if (!index.Enabled || id is null || !index.GroupItems.TryGetValue(id, out var item) ||
            item.Mime is not { } mime || item.Name is not { } name ||
            !index.Entries.Any(entry => entry.Id == id && entry.CopiedAt > Cutoff(now))) return null;
        return File.Exists(PathFor(id)) ? new Abstractions.ClipboardAttachment(mime, name, File.ReadAllBytes(PathFor(id))) : null;
    }

    static string Key(string origin, long sequence, string? group) => Convert.ToHexString(PlatformCrypto.Sha256(
        System.Text.Encoding.UTF8.GetBytes((group ?? "") + ":" + origin + ":" + sequence.ToString(CultureInfo.InvariantCulture))));

    public ClipboardHistoryPage Read(int offset, long now)
    {
        Cleanup(now);
        var rows = index.Enabled
            ? index.Entries.Where(entry => entry.CopiedAt > Cutoff(now)).OrderByDescending(entry => entry.CopiedAt)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal).Skip(Math.Clamp(offset, 0, Capacity)).Take(PageSize + 1).ToArray()
            : [];
        return new ClipboardHistoryPage(index.Enabled, index.RetentionDays,
            rows.Take(PageSize).Select(entry => entry with { Source = sourceName(entry.Origin) }).ToArray(),
            rows.Length > PageSize ? offset + PageSize : null);
    }

    public string? ReadText(string? id, long now)
    {
        if (!index.Enabled || id is null || (index.GroupItems.TryGetValue(id, out var item) && item.Mime is not null) ||
            !index.Entries.Any(entry => entry.Id == id && entry.CopiedAt > Cutoff(now)))
        {
            return null;
        }

        return File.Exists(PathFor(id)) ? File.ReadAllText(PathFor(id)) : null;
    }

    public void Cleanup(long now, bool force = false)
    {
        if (!force && now < nextCleanup)
        {
            return;
        }

        var cutoff = Cutoff(now);
        var retired = index.Entries.OrderByDescending(entry => entry.CopiedAt)
            .Where((entry, position) => entry.CopiedAt <= cutoff || position >= Capacity).ToArray();
        foreach (var entry in retired)
        {
            File.Delete(PathFor(entry.Id));
            index.Entries.Remove(entry);
            index.GroupItems.Remove(entry.Id);
        }

        if (Directory.Exists(directory))
        {
            var live = index.Entries.Select(entry => PathFor(entry.Id)).ToHashSet(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (file != indexPath && !live.Contains(file))
                {
                    File.Delete(file);
                }
            }
        }

        if (retired.Length > 0)
        {
            Save();
        }

        nextCleanup = now + (long)TimeSpan.FromDays(1).TotalMilliseconds;
    }

    long Cutoff(long now) => now - (long)TimeSpan.FromDays(index.RetentionDays).TotalMilliseconds;

    string PathFor(string id) => Path.Combine(directory, id + ".txt");

    void EnsureDirectory()
    {
        Directory.CreateDirectory(directory);
        IdentityStore.SetDirectoryMode(directory);
    }

    void Save()
    {
        EnsureDirectory();
        WritePrivate(indexPath, JsonSerializer.SerializeToUtf8Bytes(index, HistoryJsonContext.Default.HistoryIndex));
    }

    static void WritePrivate(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(temporary, options))
        {
            stream.Write(bytes);
        }

        File.Move(temporary, path, overwrite: true);
    }

    static HistoryIndex Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize(File.ReadAllBytes(path), HistoryJsonContext.Default.HistoryIndex);
                if (stored is { RetentionDays: >= 1 and <= 30, Entries: not null, GroupItems: not null } && stored.Entries.All(entry =>
                        entry is { Id.Length: 64 } && entry.Id.All(Uri.IsHexDigit)))
                {
                    return stored;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new HistoryIndex();
    }
}

internal sealed class HistoryIndex
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = 7;
    public List<ClipboardHistoryEntry> Entries { get; set; } = [];
    public Dictionary<string, ClipboardGroupItem> GroupItems { get; set; } = [];
}

[JsonSerializable(typeof(HistoryIndex))]
internal partial class HistoryJsonContext : JsonSerializerContext;
