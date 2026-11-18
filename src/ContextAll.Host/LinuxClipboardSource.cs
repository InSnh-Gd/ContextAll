using System.Text;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class LinuxClipboardSource : IClipboardSource
{
    readonly WaylandClipboard? wayland;
    readonly X11Clipboard? x11;
    readonly ClipboardFileCache files = new();

    public LinuxClipboardSource(WaylandClipboard? wayland)
        : this(wayland, null)
    {
    }

    public LinuxClipboardSource(WaylandClipboard? wayland, X11Clipboard? x11)
    {
        this.wayland = wayland;
        this.x11 = x11;
        if (this.wayland is { IsOpen: true })
        {
            this.wayland.PlainText += OnBackendPlainText;
            this.wayland.AttachmentChanged += OnBackendAttachment;
            BackendKind = ClipboardBackendKind.Wayland;
        }
        else if (this.x11 is { IsOpen: true })
        {
            this.x11.PlainText += OnBackendPlainText;
            this.x11.AttachmentChanged += OnBackendAttachment;
            BackendKind = ClipboardBackendKind.X11;
        }
        else
        {
            BackendKind = ClipboardBackendKind.None;
        }
    }

    public static LinuxClipboardSource TryOpen(string? waylandDisplay, string? runtimeDir, string? x11Display = null)
    {
        var wl = WaylandClipboard.TryOpen(waylandDisplay, runtimeDir);
        if (wl is { IsOpen: true })
        {
            return new LinuxClipboardSource(wl, null);
        }

        var x11Backend = X11Clipboard.TryOpen(x11Display);
        return new LinuxClipboardSource(null, x11Backend);
    }

    public ClipboardBackendKind BackendKind { get; }

    public string BackendName => BackendKind switch
    {
        ClipboardBackendKind.Wayland => "wayland",
        ClipboardBackendKind.X11 => "x11",
        _ => "unavailable",
    };

    public bool IsSupported => BackendKind != ClipboardBackendKind.None;

    public event Action<string, byte[]>? ClipboardChanged;
    public event Action<string>? PlainText;
    public event Action<ClipboardAttachment>? AttachmentChanged;

    void OnBackendAttachment(ClipboardAttachment attachment) => AttachmentChanged?.Invoke(attachment);

    public void CleanAttachmentCache(int retentionDays) => files.Cleanup(retentionDays);

    public async ValueTask<bool> ApplyAttachmentAsync(ClipboardAttachment attachment, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (mime, data) = ClipboardMime.Encode(attachment, files.Directory);
        var accepted = wayland is { IsOpen: true } ? await wayland.ApplyDataAsync(mime, data, ct).ConfigureAwait(false) :
            x11 is { IsOpen: true } && await x11.ApplyAttachmentAsync(mime, data, ct).ConfigureAwait(false);
        if (accepted) files.Remember(attachment);
        return accepted;
    }

    void OnBackendPlainText(string text)
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
        else if (x11 is { IsOpen: true })
        {
            x11.SetText(text);
        }
    }

    public Task SetClipboardAsync(string mime, byte[] data, CancellationToken ct = default)
    {
        if (IsSupported && (mime == WaylandClipboard.PlainMime || mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)))
        {
            SetText(Encoding.UTF8.GetString(data));
        }

        return Task.CompletedTask;
    }

    public ValueTask<string?> ReadTextAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<string?>(null);

    public ValueTask<bool> ApplyTextAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (wayland is { IsOpen: true })
        {
            return wayland.ApplyDataAsync(WaylandClipboard.PlainMime, Encoding.UTF8.GetBytes(text), ct);
        }

        return x11 is { IsOpen: true } ? x11.ApplyTextAsync(text, ct) : ValueTask.FromResult(false);
    }

    public ValueTask WriteTextAsync(string text, CancellationToken ct = default)
    {
        SetText(text);
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (wayland is not null)
        {
            wayland.PlainText -= OnBackendPlainText;
            wayland.AttachmentChanged -= OnBackendAttachment;
            wayland.Dispose();
        }

        if (x11 is not null)
        {
            x11.PlainText -= OnBackendPlainText;
            x11.AttachmentChanged -= OnBackendAttachment;
            x11.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
