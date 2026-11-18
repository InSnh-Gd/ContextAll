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

    public uint Id { get; }
    public ClipboardGroupItem Item { get; }
    public bool Complete => stream.Length == Item.Bytes;

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
