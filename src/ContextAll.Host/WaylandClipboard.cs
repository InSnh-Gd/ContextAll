using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using ContextAll.Protocol;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class WaylandClipboard : IDisposable
{
    public const string PlainMime = "text/plain;charset=utf-8";
    internal const string SensitiveMime = "x-kde-passwordManagerHint";
    const string LocalMime = "application/x-contextall-owned-selection";

    public static bool IsPlainText(string mime) => mime is "text/plain" or PlainMime;

    public static string PreferPlain(string current, string incoming)
    {
        if (incoming == PlainMime)
        {
            return PlainMime;
        }

        if (incoming == "text/plain" && current != PlainMime)
        {
            return "text/plain";
        }

        return current;
    }

    public static string? PreferredManager(IReadOnlyList<string> interfaces)
    {
        string? wlr = null;
        foreach (var name in interfaces)
        {
            if (name == "ext_data_control_manager_v1")
            {
                return name;
            }

            if (name == "zwlr_data_control_manager_v1")
            {
                wlr = name;
            }
        }

        return wlr;
    }

    readonly WaylandSocket socket;
    readonly Dictionary<uint, string> offers = [];
    readonly HashSet<uint> sensitiveOffers = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte[]> sources = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<uint, TaskCompletionSource<bool>> writes = new();
    readonly object gate = new();
    uint nextId = 2;
    uint registry;
    uint seat;
    uint manager;
    uint device;
    volatile uint selection;
    uint syncCallback;
    readonly List<(uint Name, string Interface)> globals = [];
    bool chosen;
    readonly ManualResetEventSlim ready = new(false);
    bool running = true;

    WaylandClipboard(WaylandSocket socket)
    {
        this.socket = socket;
    }

    public bool IsOpen { get; private set; }

    public event Action<string>? PlainText;
    public event Action<ClipboardAttachment>? AttachmentChanged;

    public static WaylandClipboard? TryOpen(string? display, string? runtimeDirectory)
    {
        if (string.IsNullOrEmpty(display))
        {
            return null;
        }

        var path = display.Contains('/', StringComparison.Ordinal)
            ? display
            : Path.Combine(runtimeDirectory ?? "", display);
        try
        {
            var clipboard = new WaylandClipboard(WaylandSocket.Connect(path));
            clipboard.Start();
            return clipboard.IsOpen ? clipboard : null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or FileNotFoundException)
        {
            return null;
        }
    }

    public void SetText(string text) => TrySetText(text);

    internal bool TrySetText(string text) => TrySetData(PlainMime, Encoding.UTF8.GetBytes(text));

    internal bool TrySetData(string mime, byte[] bytes)
    {
        lock (gate)
        {
            if (!IsOpen)
            {
                return false;
            }

            var source = nextId++;
            sources[source] = bytes;
            socket.Send(WaylandWire.Message(manager, 0, writer => writer.U32(source)));
            socket.Send(WaylandWire.Message(source, 0, writer => writer.Text(mime)));
            socket.Send(WaylandWire.Message(source, 0, writer => writer.Text(LocalMime)));
            socket.Send(WaylandWire.Message(device, 0, writer => writer.U32(source)));
            return true;
        }
    }

    internal async ValueTask<bool> ApplyDataAsync(string mime, byte[] bytes, CancellationToken ct)
    {
        uint callback = 0;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            lock (gate)
            {
                if (!TrySetData(mime, bytes)) return false;
                callback = nextId++;
                writes[callback] = completion;
                socket.Send(WaylandWire.Message(1, 0, writer => writer.U32(callback)));
            }

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            if (callback != 0) writes.TryRemove(callback, out _);
        }
    }

    public void Dispose()
    {
        running = false;
        ready.Set();
        socket.Dispose();
    }

    void Start()
    {
        registry = nextId++;
        socket.Send(WaylandWire.Message(1, 1, writer => writer.U32(registry)));
        syncCallback = nextId++;
        socket.Send(WaylandWire.Message(1, 0, writer => writer.U32(syncCallback)));
        _ = Task.Run(Read);
        if (!ready.Wait(TimeSpan.FromSeconds(2)))
        {
            Dispose();
            return;
        }

        IsOpen = manager != 0 && device != 0;
        if (!IsOpen)
        {
            Dispose();
        }
    }

    void Read()
    {
        try
        {
            while (running)
            {
                var message = socket.Receive();
                if (message is null)
                {
                    return;
                }

                Handle(message);
            }
        }
        catch (Exception)
        {
            ready.Set();
        }
        finally
        {
            IsOpen = false;
            foreach (var write in writes.Values) write.TrySetResult(false);
        }
    }

    void Handle(WaylandMessage message)
    {
        if (message.Opcode == 0 && writes.TryRemove(message.ObjectId, out var write))
        {
            write.TrySetResult(true);
        }
        else if (message.ObjectId == registry && message.Opcode == 0)
        {
            var offset = 0;
            var name = WaylandWire.U32(message.Body, ref offset);
            var interfaceName = WaylandWire.Text(message.Body, ref offset);
            if (!chosen)
            {
                globals.Add((name, interfaceName));
            }
        }
        else if (message.ObjectId == syncCallback && syncCallback != 0 && message.Opcode == 0)
        {
            if (!chosen)
            {
                chosen = true;
                BindChosen();
                if (manager == 0)
                {
                    ready.Set();
                }

                return;
            }

            ready.Set();
        }
        else if (message.ObjectId == device && message.Opcode == 0)
        {
            var offset = 0;
            offers[WaylandWire.U32(message.Body, ref offset)] = "";
        }
        else if (offers.ContainsKey(message.ObjectId) && message.Opcode == 0)
        {
            var offset = 0;
            var mime = WaylandWire.Text(message.Body, ref offset);
            if (mime is SensitiveMime or LocalMime)
            {
                sensitiveOffers.Add(message.ObjectId);
            }
            var current = offers[message.ObjectId];
            offers[message.ObjectId] = mime == ClipboardAttachment.Image ||
                (mime == ClipboardMime.UriList && current != ClipboardAttachment.Image) ? mime :
                current is ClipboardAttachment.Image or ClipboardMime.UriList ? current : PreferPlain(current, mime);
        }
        else if (message.ObjectId == device && message.Opcode == 1)
        {
            var offset = 0;
            var offer = WaylandWire.U32(message.Body, ref offset);
            if (selection != 0 && selection != offer)
            {
                socket.Send(WaylandWire.Message(selection, 1));
                offers.Remove(selection);
                sensitiveOffers.Remove(selection);
            }

            selection = offer;
            if (offer == 0)
            {
                PlainText?.Invoke("");
                return;
            }

            if (sensitiveOffers.Contains(offer) || !offers.TryGetValue(offer, out var mime) ||
                (!IsPlainText(mime) && mime is not (ClipboardAttachment.Image or ClipboardMime.UriList)))
            {
                return;
            }

            var pipes = new int[2];
            if (Native.pipe2(pipes, 0x80000) != 0)
            {
                return;
            }

            socket.Send(WaylandWire.Message(offer, 0, writer => writer.Text(mime)), pipes[1]);
            Native.close(pipes[1]);
            var read = pipes[0];
            _ = Task.Run(() =>
            {
                try
                {
                    var bytes = ReadAll(read, mime == ClipboardAttachment.Image ? FrameLimits.MaxClipboardAttachmentBytes : FrameLimits.MaxClipboardBytes);
                    if (bytes is not null && offer == selection)
                    {
                        if (IsPlainText(mime)) PlainText?.Invoke(new UTF8Encoding(false, true).GetString(bytes));
                        else if (ClipboardMime.Decode(mime, bytes) is { } attachment && offer == selection) AttachmentChanged?.Invoke(attachment);
                    }
                }
                catch (DecoderFallbackException) { }
                finally
                {
                    Native.close(read);
                }
            });
        }
        else if (sources.ContainsKey(message.ObjectId) && message.Opcode == 0)
        {
            var file = socket.TakeFile();
            if (file < 0)
            {
                return;
            }

            try
            {
                var offset = 0;
                var mime = WaylandWire.Text(message.Body, ref offset);
                if (mime == LocalMime || !sources.TryGetValue(message.ObjectId, out var bytes)) return;
                var written = 0;
                var chunk = new byte[64 * 1024];
                while (written < bytes.Length)
                {
                    var length = Math.Min(chunk.Length, bytes.Length - written);
                    bytes.AsSpan(written, length).CopyTo(chunk);
                    var count = Native.write(file, chunk, (nuint)length);
                    if (count <= 0)
                    {
                        break;
                    }

                    written += (int)count;
                }
            }
            finally
            {
                Native.close(file);
            }
        }
        else if (message.Opcode == 1 && sources.TryRemove(message.ObjectId, out _))
        {
            socket.Send(WaylandWire.Message(message.ObjectId, 1));
        }
    }

    static byte[]? ReadAll(int fd, int limit)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var count = Native.read(fd, buffer, (nuint)buffer.Length);
            if (count < 0 || stream.Length + (long)count > limit)
            {
                return null;
            }

            if (count == 0)
            {
                break;
            }

            stream.Write(buffer, 0, (int)count);
        }

        return stream.ToArray();
    }

    static class Native
    {
        [DllImport("libc", SetLastError = true)]
        public static extern int pipe2(int[] fds, int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern nint write(int fd, byte[] buffer, nuint count);

        [DllImport("libc", SetLastError = true)]
        public static extern nint read(int fd, byte[] buffer, nuint count);
    }

    void BindChosen()
    {
        var names = new string[globals.Count];
        for (var i = 0; i < globals.Count; i++)
        {
            names[i] = globals[i].Interface;
        }

        var preferred = PreferredManager(names);
        if (preferred is null || !TryGlobal("wl_seat", out var seatName) || !TryGlobal(preferred, out var managerName))
        {
            return;
        }

        seat = nextId++;
        SendBind(seatName, "wl_seat", seat);
        manager = nextId++;
        SendBind(managerName, preferred, manager);
        device = nextId++;
        socket.Send(WaylandWire.Message(manager, 1, writer =>
        {
            writer.U32(device);
            writer.U32(seat);
        }));
        syncCallback = nextId++;
        socket.Send(WaylandWire.Message(1, 0, writer => writer.U32(syncCallback)));
    }

    bool TryGlobal(string interfaceName, out uint name)
    {
        foreach (var global in globals)
        {
            if (global.Interface == interfaceName)
            {
                name = global.Name;
                return true;
            }
        }

        name = 0;
        return false;
    }

    void SendBind(uint name, string interfaceName, uint id)
    {
        socket.Send(WaylandWire.Message(registry, 0, writer =>
        {
            writer.U32(name);
            writer.Text(interfaceName);
            writer.U32(1);
            writer.U32(id);
        }));
    }
}
