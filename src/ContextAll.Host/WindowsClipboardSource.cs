using System.Runtime.InteropServices;
using System.Text;
using System.Buffers.Binary;
using ContextAll.Protocol;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

internal interface IWindowsClipboard
{
    uint Sequence { get; }
    bool TryRead(out uint sequence, out string? text);
    bool TryWrite(string text, out uint sequence);
    bool TryReadAttachment(uint sequence, out ClipboardAttachment? attachment) { attachment = null; return true; }
    bool TryWriteAttachment(ClipboardAttachment attachment, string path, out uint sequence) { sequence = 0; return false; }
}

internal sealed class WindowsClipboardSource : IClipboardSource
{
    readonly IWindowsClipboard desktop;
    readonly Timer timer;
    readonly object gate = new();
    readonly ClipboardFileCache files = new();
    uint sequence;
    bool disposed;

    internal WindowsClipboardSource(IWindowsClipboard desktop)
    {
        this.desktop = desktop;
        sequence = desktop.Sequence;
        timer = new Timer(_ => Poll(), null, 100, 100);
    }

    public static WindowsClipboardSource? TryOpen() =>
        OperatingSystem.IsWindows() && Environment.UserInteractive
            ? new WindowsClipboardSource(new Win32Clipboard())
            : null;

    public string BackendName => "windows";
    public bool IsSupported => true;
    public event Action<string>? PlainText;
    public event Action<ClipboardAttachment>? AttachmentChanged;
    public event Action<string, byte[]>? ClipboardChanged { add { } remove { } }

    void Poll()
    {
        string? text;
        ClipboardAttachment? attachment = null;
        lock (gate)
        {
            if (disposed || desktop.Sequence == sequence || !desktop.TryRead(out var captured, out text))
            {
                return;
            }

            if (text is null && !desktop.TryReadAttachment(captured, out attachment)) return;
            sequence = captured;
        }

        if (attachment is not null)
        {
            AttachmentChanged?.Invoke(attachment);
        }
        else if (text is not null && Encoding.UTF8.GetByteCount(text) <= FrameLimits.MaxClipboardBytes)
        {
            PlainText?.Invoke(text);
        }
    }

    public void SetText(string text) => _ = ApplyTextAsync(text);

    public void CleanAttachmentCache(int retentionDays) => files.Cleanup(retentionDays);

    public async ValueTask<bool> ApplyAttachmentAsync(ClipboardAttachment attachment, CancellationToken ct = default)
    {
        var path = attachment.Mime == ClipboardAttachment.FileType ? attachment.PublishFile(files.Directory) : "";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (disposed) return false;
                if (desktop.TryWriteAttachment(attachment, path, out var written))
                {
                    sequence = written;
                    files.Remember(attachment);
                    return true;
                }
            }

            await Task.Delay(25, ct).ConfigureAwait(false);
        }

        return false;
    }

    public ValueTask<string?> ReadTextAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(!disposed && desktop.TryRead(out _, out var text) ? text : null);
        }
    }

    public async Task SetClipboardAsync(string mime, byte[] data, CancellationToken ct = default)
    {
        if (mime is "text/plain" or "text/plain;charset=utf-8")
        {
            await ApplyTextAsync(Encoding.UTF8.GetString(data), ct).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> ApplyTextAsync(string text, CancellationToken ct = default)
    {
        if (text.Contains('\0') || Encoding.UTF8.GetByteCount(text) > FrameLimits.MaxClipboardBytes)
        {
            return false;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (disposed)
                {
                    return false;
                }

                if (desktop.TryWrite(text, out var written))
                {
                    sequence = written;
                    return true;
                }
            }

            await Task.Delay(25, ct).ConfigureAwait(false);
        }

        return false;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            timer.Dispose();
        }
    }

    sealed partial class Win32Clipboard : IWindowsClipboard
    {
        const uint UnicodeText = 13;
        const uint Bitmap = 2;
        const uint FileDrop = 15;
        readonly uint exclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        readonly uint history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
        readonly uint cloud = RegisterClipboardFormat("CanUploadToCloudClipboard");
        readonly uint png = RegisterClipboardFormat("PNG");

        public uint Sequence => GetClipboardSequenceNumber();

        public unsafe bool TryRead(out uint sequence, out string? text)
        {
            text = null;
            sequence = 0;
            if (OpenClipboard(0) == 0)
            {
                return false;
            }

            try
            {
                sequence = Sequence;
                if (IsClipboardFormatAvailable(FileDrop) != 0 || IsClipboardFormatAvailable(Bitmap) != 0 ||
                    (png != 0 && IsClipboardFormatAvailable(png) != 0) || IsSensitive())
                {
                    return true;
                }

                var handle = GetClipboardData(UnicodeText);
                if (handle == 0)
                {
                    return IsClipboardFormatAvailable(UnicodeText) == 0;
                }

                var pointer = GlobalLock(handle);
                if (pointer == 0)
                {
                    return false;
                }

                try
                {
                    var count = (int)Math.Min(GlobalSize(handle) / sizeof(char), (nuint)FrameLimits.MaxClipboardBytes + 1);
                    var chars = new ReadOnlySpan<char>((void*)pointer, count);
                    var end = chars.IndexOf('\0');
                    if (end >= 0)
                    {
                        text = new string(chars[..end]);
                    }
                    return true;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        bool IsSensitive() => (exclude != 0 && IsClipboardFormatAvailable(exclude) != 0) || Denied(history) || Denied(cloud);

        public bool TryReadAttachment(uint sequence, out ClipboardAttachment? attachment)
        {
            attachment = null;
            if (OpenClipboard(0) == 0) return false;
            string? path = null;
            try
            {
                if (Sequence != sequence) return false;
                if (IsSensitive()) return true;
                if (png != 0 && IsClipboardFormatAvailable(png) != 0)
                {
                    var handle = GetClipboardData(png);
                    if (handle == 0) return false;
                    var size = GlobalSize(handle);
                    if (size is 0 or > FrameLimits.MaxClipboardAttachmentBytes) return true;
                    var data = GlobalLock(handle);
                    if (data == 0) return false;
                    try
                    {
                        var bytes = new byte[(int)size];
                        Marshal.Copy(data, bytes, 0, bytes.Length);
                        var image = new ClipboardAttachment(ClipboardAttachment.Image, "clipboard.png", bytes);
                        if (image.IsValid()) attachment = image;
                    }
                    finally { GlobalUnlock(handle); }

                    return true;
                }

                if (IsClipboardFormatAvailable(FileDrop) != 0)
                {
                    var drop = GetClipboardData(FileDrop);
                    if (drop == 0) return false;
                    if (DragQueryFile(drop, uint.MaxValue, null, 0) != 1) return true;
                    var length = DragQueryFile(drop, 0, null, 0);
                    if (length is 0 or > 32767) return true;
                    var buffer = new StringBuilder((int)length + 1);
                    if (DragQueryFile(drop, 0, buffer, length + 1) == length) path = buffer.ToString();
                }
            }
            finally { CloseClipboard(); }

            if (path is not null) attachment = ClipboardAttachment.ReadFile(path);
            return true;
        }

        static bool Denied(uint format)
        {
            if (format == 0 || IsClipboardFormatAvailable(format) == 0)
            {
                return false;
            }

            var handle = GetClipboardData(format);
            if (handle == 0 || GlobalSize(handle) < sizeof(uint))
            {
                return true;
            }

            var data = GlobalLock(handle);
            if (data == 0)
            {
                return true;
            }

            try { return Marshal.ReadInt32(data) == 0; }
            finally { GlobalUnlock(handle); }
        }

        public bool TryWrite(string text, out uint sequence) => TryWriteData(UnicodeText, Encoding.Unicode.GetBytes(text + '\0'), out sequence);

        public bool TryWriteAttachment(ClipboardAttachment attachment, string path, out uint sequence)
        {
            if (attachment.Mime == ClipboardAttachment.Image) return TryWriteData(png, attachment.Bytes, out sequence);
            var names = Encoding.Unicode.GetBytes(path + "\0\0");
            var drop = new byte[20 + names.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(drop, 20);
            BinaryPrimitives.WriteUInt32LittleEndian(drop.AsSpan(16), 1);
            names.CopyTo(drop.AsSpan(20));
            return TryWriteData(FileDrop, drop, out sequence);
        }

        static bool TryWriteData(uint format, byte[] data, out uint sequence)
        {
            sequence = 0;
            if (format == 0) return false;
            var owner = CreateWindow(0, "STATIC", null, 0, 0, 0, 0, 0, -3, 0, 0, 0);
            if (owner == 0)
            {
                return false;
            }

            var handle = GlobalAlloc(2, (nuint)data.Length);
            try
            {
                var pointer = handle == 0 ? 0 : GlobalLock(handle);
                if (pointer == 0)
                {
                    return false;
                }

                Marshal.Copy(data, 0, pointer, data.Length);
                GlobalUnlock(handle);

                if (OpenClipboard(owner) == 0)
                {
                    return false;
                }

                try
                {
                    if (EmptyClipboard() == 0 || SetClipboardData(format, handle) == 0)
                    {
                        return false;
                    }

                    handle = 0;
                    sequence = GetClipboardSequenceNumber();
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            finally
            {
                if (handle != 0)
                {
                    GlobalFree(handle);
                }

                DestroyWindow(owner);
            }
        }

        [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", ExactSpelling = true, CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", ExactSpelling = true, CharSet = CharSet.Unicode)] static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, uint capacity);
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", ExactSpelling = true, CharSet = CharSet.Unicode)] static extern nint CreateWindow(uint exStyle, string className, string? title, uint style, int left, int top, int width, int height, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] static extern int DestroyWindow(nint window);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern int OpenClipboard(nint owner);
        [DllImport("user32.dll")] static extern int CloseClipboard();
        [DllImport("user32.dll")] static extern int EmptyClipboard();
        [DllImport("user32.dll")] static extern int IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
        [DllImport("user32.dll")] static extern nint SetClipboardData(uint format, nint data);
        [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags, nuint size);
        [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint handle);
        [DllImport("kernel32.dll")] static extern nint GlobalLock(nint handle);
        [DllImport("kernel32.dll")] static extern int GlobalUnlock(nint handle);
        [DllImport("kernel32.dll")] static extern nint GlobalFree(nint handle);
    }
}
