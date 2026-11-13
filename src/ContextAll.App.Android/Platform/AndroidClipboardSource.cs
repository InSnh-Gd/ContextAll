using ContextAll.Session.Abstractions;

namespace ContextAll.App.Android.Platform;

public sealed class AndroidClipboardSource : IClipboardSource
{
    public bool IsSupported => false;

    public event Action<string, byte[]>? ClipboardChanged
    {
        add { }
        remove { }
    }

    public event Action<string>? PlainText
    {
        add { }
        remove { }
    }

    public Task<string?> ReadTextAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task WriteTextAsync(string text, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SetClipboardAsync(string mimeType, byte[] data, CancellationToken ct = default) =>
        Task.CompletedTask;

    public void SetText(string text)
    {
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

