using ContextAll.Protocol;

namespace ContextAll.Session;

internal sealed class IncomingClipboardItem : IDisposable
{
    readonly Stream stream;

    public IncomingClipboardItem(uint id, ClipboardGroupItem item, string stateDirectory)
    {
        Id = id;
        Item = item;
        if (item.Mime is null)
        {
            stream = new MemoryStream(item.Bytes);
            return;
        }

        var directory = Path.Combine(stateDirectory, "clipboard-incoming");
        Directory.CreateDirectory(directory);
        IdentityStore.SetDirectoryMode(directory);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        stream = new FileStream(Path.Combine(directory, Guid.NewGuid().ToString("N")), options);
    }

    public uint Id { get; private set; }
    public ClipboardGroupItem Item { get; private set; }
    public bool Complete => stream.Length == Item.Bytes;
    public ClipboardGroupResume? Resume => Item.Mime is not null && stream.Length > 0 && !Complete
        ? new(Item.Group, Item.Origin, Item.Sequence, Item.Sha256, checked((int)stream.Length)) : null;

    public bool TryResume(uint id, ClipboardGroupItem item)
    {
        if (item.Offset <= 0 || item.Offset != stream.Length ||
            item with { Replay = false, RelayedAt = 0, Offset = 0 } != Item with { Replay = false, RelayedAt = 0, Offset = 0 })
        {
            return false;
        }

        Id = id;
        Item = item with { Offset = 0 };
        return true;
    }

    public bool Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > Item.Bytes - stream.Length) return false;
        stream.Write(bytes);
        return true;
    }

    public byte[] Read()
    {
        if (!Complete) throw new InvalidDataException("clipboard-item-incomplete");
        var bytes = new byte[Item.Bytes];
        stream.Position = 0;
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Dispose() => stream.Dispose();
}
