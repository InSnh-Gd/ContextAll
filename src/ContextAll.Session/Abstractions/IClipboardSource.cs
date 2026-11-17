namespace ContextAll.Session.Abstractions;

public interface IClipboardSource : IAsyncDisposable, IDisposable
{
    bool IsSupported { get; }
    string BackendName => IsSupported ? "wayland" : "unavailable";
    event Action<string, byte[]>? ClipboardChanged;
    event Action<string>? PlainText;
    void SetText(string text);
    Task SetClipboardAsync(string mime, byte[] data, CancellationToken ct = default);
    ValueTask<string?> ReadTextAsync(CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
    ValueTask WriteTextAsync(string text, CancellationToken ct = default)
    {
        SetText(text);
        return ValueTask.CompletedTask;
    }
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
    void IDisposable.Dispose() { }
}
