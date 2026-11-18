using ContextAll.Core;

namespace ContextAll.Host.Tests;

public sealed class ClipboardWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_remote_write_is_dispatched_once_and_only_confirmed_on_success(bool accepted)
    {
        var root = Directory.CreateTempSubdirectory("contextall-clipboard-");
        try
        {
            var source = new RecordingSource(accepted);
            using var host = new SessionHost(new LinuxHostPaths(
                identityDirectory: Path.Combine(root.FullName, "id"),
                settingsFile: Path.Combine(root.FullName, "config.yaml"),
                stateDirectory: Path.Combine(root.FullName, "state"),
                transfersDirectory: Path.Combine(root.FullName, "transfers"),
                defaultDownloadDirectory: Path.Combine(root.FullName, "downloads")), clipboard: source);

            Assert.Null(host.PushClipboardText("previous"));
            host.Clipboard.OnLocal(ClipboardKind.Plain, "remote", 100);
            host.WriteClipboard("remote");
            host.FlushClipboard();

            Assert.Equal(new[] { "remote" }, source.Writes);
            Assert.Equal("remote", ClipboardFiles.Read(host.ClipboardPath).Text);
            Assert.Equal(accepted ? "remote" : "previous", ClipboardFiles.Read(host.ClipboardPath).AppliedText);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    sealed class RecordingSource(bool accepted) : IClipboardSource
    {
        public bool IsSupported => true;
        public List<string> Writes { get; } = [];
        public event Action<string, byte[]>? ClipboardChanged { add { } remove { } }
        public event Action<string>? PlainText { add { } remove { } }
        public void SetText(string text) => Writes.Add(text);

        public Task SetClipboardAsync(string mime, byte[] data, CancellationToken ct = default)
        {
            Writes.Add(System.Text.Encoding.UTF8.GetString(data));
            return Task.CompletedTask;
        }

        public ValueTask<bool> ApplyTextAsync(string text, CancellationToken ct = default)
        {
            Writes.Add(text);
            return ValueTask.FromResult(accepted);
        }
    }
}
