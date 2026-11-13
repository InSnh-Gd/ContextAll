using System.Text;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class LinuxClipboardSource : IClipboardSource
{
    readonly WaylandClipboard? wayland;

    public LinuxClipboardSource(WaylandClipboard? wayland)
    {
        this.wayland = wayland;
        if (this.wayland is not null)
        {
            this.wayland.PlainText += OnWaylandPlainText;
        }
    }

    public static LinuxClipboardSource TryOpen(string? display, string? runtimeDir)
    {
        var wl = WaylandClipboard.TryOpen(display, runtimeDir);
        return new LinuxClipboardSource(wl);
    }

    public bool IsSupported => wayland is { IsOpen: true };

    public event Action<string, byte[]>? ClipboardChanged;
    public event Action<string>? PlainText;

    void OnWaylandPlainText(string text)
    {
        PlainText?.Invoke(text);
        ClipboardChanged?.Invoke(WaylandClipboard.PlainMime, Encoding.UTF8.GetBytes(text));
    }

    public void SetText(string text)
    {
        if (wayland is { IsOpen: true })
        {
            wayland.SetText(text);
        }
    }

    public Task SetClipboardAsync(string mime, byte[] data, CancellationToken ct = default)
    {
        if (wayland is { IsOpen: true } && (mime == WaylandClipboard.PlainMime || mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)))
        {
            wayland.SetText(Encoding.UTF8.GetString(data));
        }
        return Task.CompletedTask;
    }

    public ValueTask<string?> ReadTextAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<string?>(null);

    public ValueTask WriteTextAsync(string text, CancellationToken ct = default)
    {
        SetText(text);
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (wayland is not null)
        {
            wayland.PlainText -= OnWaylandPlainText;
            wayland.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
