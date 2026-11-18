namespace ContextAll.Session.Abstractions;

public sealed class ClipboardFileCache(string? directory = null)
{
    readonly object gate = new();
    DateTime nextCleanup;

    public string Directory { get; } = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "contextall", "clipboard-shared");

    public void Remember(ClipboardAttachment attachment)
    {
        if (attachment.Mime == ClipboardAttachment.FileType)
        {
            var path = attachment.PublishFile(Directory);
            System.IO.Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(path) ?? Directory, DateTime.UtcNow);
        }
    }

    public void Cleanup(int retentionDays)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if (now < nextCleanup || !System.IO.Directory.Exists(Directory)) return;
            var directories = new DirectoryInfo(Directory).EnumerateDirectories()
                .Where(item => item.Name.Length == 64 && item.Name.All(Uri.IsHexDigit) && (item.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(item => item.LastWriteTimeUtc).Skip(1);
            foreach (var item in directories.Where(item => item.LastWriteTimeUtc < now.AddDays(-retentionDays)))
            {
                item.Delete(recursive: true);
            }

            nextCleanup = now.AddDays(1);
        }
    }
}
