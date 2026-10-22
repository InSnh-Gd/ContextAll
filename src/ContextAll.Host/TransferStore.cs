using System.Security.Cryptography;
using System.Text.Json.Serialization;
using ContextAll.Core;

namespace ContextAll.Host;

public sealed class TransferMeta
{
    public string TransferId { get; set; } = "";

    public string PeerId { get; set; } = "";

    public string Direction { get; set; } = "";

    public string FileName { get; set; } = "";

    public long TotalSize { get; set; }

    public int SegmentSize { get; set; }

    public long SegmentCount { get; set; }

    public string RootSha256 { get; set; } = "";

    public string Bits { get; set; } = "";

    public long Completed { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TransferMeta))]
public partial class TransferJsonContext : JsonSerializerContext;

public sealed class TransferStore
{
    readonly string directory;

    public TransferStore(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        IdentityStore.SetDirectoryMode(directory);
    }

    public void Save(TransferSlot slot)
    {
        var meta = new TransferMeta
        {
            TransferId = slot.TransferId,
            PeerId = slot.PeerId,
            Direction = slot.Direction,
            FileName = slot.FileName,
            TotalSize = slot.TotalSize,
            SegmentSize = slot.SegmentSize,
            SegmentCount = slot.SegmentCount,
            RootSha256 = slot.RootSha256,
            Bits = Convert.ToBase64String(slot.Bits),
            Completed = slot.Completed,
        };
        var path = MetaPath(slot.TransferId);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(meta, TransferJsonContext.Default.TransferMeta);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, json);
        File.Move(temp, path, overwrite: true);
        IdentityStore.SetFileMode(path);
    }

    public void Create(TransferSlot slot, byte[]? full)
    {
        var part = PartPath(slot.TransferId);
        using (var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            if (full is not null)
            {
                stream.Write(full);
            }
            else
            {
                stream.SetLength(slot.TotalSize);
            }

            stream.Flush(true);
        }

        IdentityStore.SetFileMode(part);
        Save(slot);
    }

    public void Write(string transferId, long offset, byte[] data)
    {
        using var stream = new FileStream(PartPath(transferId), FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Position = offset;
        stream.Write(data);
        stream.Flush(true);
    }

    public byte[] Read(string transferId) => File.ReadAllBytes(PartPath(transferId));

    public bool Exists(string transferId) => File.Exists(MetaPath(transferId));

    public void Delete(string transferId)
    {
        if (File.Exists(PartPath(transferId)))
        {
            File.Delete(PartPath(transferId));
        }

        if (File.Exists(MetaPath(transferId)))
        {
            File.Delete(MetaPath(transferId));
        }
    }

    public TransferSlot? Load(string transferId)
    {
        var path = MetaPath(transferId);
        if (!File.Exists(path))
        {
            return null;
        }

        var meta = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllBytes(path), TransferJsonContext.Default.TransferMeta);
        if (meta is null)
        {
            return null;
        }

        return new TransferSlot
        {
            TransferId = meta.TransferId,
            PeerId = meta.PeerId,
            Direction = meta.Direction,
            FileName = meta.FileName,
            TotalSize = meta.TotalSize,
            SegmentSize = meta.SegmentSize,
            SegmentCount = meta.SegmentCount,
            RootSha256 = meta.RootSha256,
            Bits = string.IsNullOrEmpty(meta.Bits) ? [] : Convert.FromBase64String(meta.Bits),
            Completed = meta.Completed,
            Accepted = true,
        };
    }

    public List<TransferSlot> Incomplete(string peerId, string direction)
    {
        var slots = new List<TransferSlot>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.meta.json"))
        {
            var id = Path.GetFileName(path)[..^".meta.json".Length];
            var slot = Load(id);
            if (slot is null || slot.PeerId != peerId || slot.Direction != direction || slot.Completed >= slot.SegmentCount)
            {
                continue;
            }

            slots.Add(slot);
        }

        return slots;
    }

    public bool TryPlace(TransferSlot slot, string downloads)
    {
        var part = PartPath(slot.TransferId);
        if (!File.Exists(part) || new FileInfo(part).Length != slot.TotalSize)
        {
            return false;
        }

        using var sha = SHA256.Create();
        using (var stream = File.OpenRead(part))
        {
            var hash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            if (hash != slot.RootSha256)
            {
                return false;
            }
        }

        Directory.CreateDirectory(downloads);
        var name = DownloadNames.AvoidCollision(DownloadNames.Sanitize(slot.FileName), candidate => File.Exists(Path.Combine(downloads, candidate)));
        var target = Path.Combine(downloads, name);
        try
        {
            File.Move(part, target);
        }
        catch (IOException)
        {
            var temporary = Path.Combine(downloads, name + ".part-" + slot.TransferId);
            File.Copy(part, temporary, overwrite: true);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Flush(true);
            }

            File.Move(temporary, target, overwrite: false);
            File.Delete(part);
        }

        Delete(slot.TransferId);
        return true;
    }

    string PartPath(string transferId) => Path.Combine(directory, transferId + ".part");

    string MetaPath(string transferId) => Path.Combine(directory, transferId + ".meta.json");
}
