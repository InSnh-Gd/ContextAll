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
public sealed partial class TransferJsonContext : JsonSerializerContext;

public sealed class TransferStore
{
    readonly string directory;
    readonly Dictionary<string, FileStream> parts = [];
    readonly object gate = new();

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
        lock (gate)
        {
            ClosePart(slot.TransferId);
            using var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.Read);
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

    public void Write(string transferId, long offset, ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            var stream = Open(transferId);
            stream.Position = offset;
            stream.Write(data);
            stream.Flush();
        }
    }

    public void Flush(string transferId)
    {
        lock (gate)
        {
            if (parts.TryGetValue(transferId, out var stream))
            {
                stream.Flush();
            }
        }
    }

    public byte[] Read(string transferId)
    {
        lock (gate)
        {
            if (parts.TryGetValue(transferId, out var stream))
            {
                var position = stream.Position;
                stream.Position = 0;
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                stream.Position = position;
                return bytes;
            }
        }

        return File.ReadAllBytes(PartPath(transferId));
    }

    public bool Exists(string transferId) => File.Exists(MetaPath(transferId));

    public void Delete(string transferId)
    {
        lock (gate)
        {
            ClosePart(transferId);
            if (File.Exists(PartPath(transferId)))
            {
                File.Delete(PartPath(transferId));
            }

            if (File.Exists(MetaPath(transferId)))
            {
                File.Delete(MetaPath(transferId));
            }
        }
    }

    public void DeletePeer(string peerId)
    {
        foreach (var path in Directory.GetFiles(directory, "*.meta.json"))
        {
            var name = Path.GetFileName(path);
            const string suffix = ".meta.json";
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var id = name[..^suffix.Length];
            var slot = Load(id);
            if (slot is not null && slot.PeerId == peerId)
            {
                Delete(id);
            }
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
        lock (gate)
        {
            if (!File.Exists(part))
            {
                return false;
            }

            if (parts.TryGetValue(slot.TransferId, out var open))
            {
                open.Flush();
                if (open.Length != slot.TotalSize)
                {
                    return false;
                }

                open.Position = 0;
                var openHash = Convert.ToHexString(SHA256.HashData(open)).ToLowerInvariant();
                if (openHash != slot.RootSha256)
                {
                    return false;
                }
            }
            else if (new FileInfo(part).Length != slot.TotalSize)
            {
                return false;
            }
            else
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(part);
                var hash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
                if (hash != slot.RootSha256)
                {
                    return false;
                }
            }

            ClosePart(slot.TransferId);
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

    FileStream Open(string transferId)
    {
        if (!parts.TryGetValue(transferId, out var stream))
        {
            stream = new FileStream(PartPath(transferId), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            parts.Add(transferId, stream);
        }

        return stream;
    }

    void ClosePart(string transferId)
    {
        if (parts.Remove(transferId, out var stream))
        {
            stream.Dispose();
        }
    }
}
