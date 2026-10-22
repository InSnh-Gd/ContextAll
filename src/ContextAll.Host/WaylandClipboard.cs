using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextAll.Host;

public sealed class WaylandClipboard : IDisposable
{
    public const string PlainMime = "text/plain;charset=utf-8";

    readonly WaylandSocket socket;
    readonly Dictionary<uint, string> offers = [];
    readonly Dictionary<uint, string> sources = [];
    readonly object gate = new();
    uint nextId = 2;
    uint registry;
    uint seat;
    uint manager;
    uint device;
    uint selection;
    uint syncCallback;
    readonly ManualResetEventSlim ready = new(false);
    bool running = true;

    WaylandClipboard(WaylandSocket socket)
    {
        this.socket = socket;
    }

    public bool IsOpen { get; private set; }

    public event Action<string>? PlainText;

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

    public void SetText(string text)
    {
        lock (gate)
        {
            if (!IsOpen)
            {
                return;
            }

            var source = nextId++;
            sources[source] = text;
            socket.Send(WaylandWire.Message(manager, 0, writer => writer.U32(source)));
            socket.Send(WaylandWire.Message(source, 0, writer => writer.Text(PlainMime)));
            socket.Send(WaylandWire.Message(device, 0, writer => writer.U32(source)));
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
    }

    void Handle(WaylandMessage message)
    {
        if (message.ObjectId == registry && message.Opcode == 0)
        {
            var offset = 0;
            var name = WaylandWire.U32(message.Body, ref offset);
            var interfaceName = WaylandWire.Text(message.Body, ref offset);
            if (interfaceName == "wl_seat" && seat == 0)
            {
                seat = nextId++;
                socket.Send(WaylandWire.Message(registry, 0, writer =>
                {
                    writer.U32(name);
                    writer.Text("wl_seat");
                    writer.U32(1);
                    writer.U32(seat);
                }));
                BindDevice();
            }
            else if (interfaceName == "zwlr_data_control_manager_v1" && manager == 0)
            {
                manager = nextId++;
                socket.Send(WaylandWire.Message(registry, 0, writer =>
                {
                    writer.U32(name);
                    writer.Text(interfaceName);
                    writer.U32(1);
                    writer.U32(manager);
                }));
                BindDevice();
            }
        }
        else if (message.ObjectId == syncCallback && syncCallback != 0 && message.Opcode == 0)
        {
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
            if (offers[message.ObjectId] != PlainMime)
            {
                offers[message.ObjectId] = mime;
            }
        }
        else if (message.ObjectId == device && message.Opcode == 1)
        {
            var offset = 0;
            var offer = WaylandWire.U32(message.Body, ref offset);
            if (selection != 0 && selection != offer)
            {
                socket.Send(WaylandWire.Message(selection, 1));
                offers.Remove(selection);
            }

            selection = offer;
            if (offer == 0)
            {
                PlainText?.Invoke("");
                return;
            }

            if (!offers.TryGetValue(offer, out var mime) || mime != PlainMime)
            {
                return;
            }

            var pipes = new int[2];
            if (Native.pipe2(pipes, 0x80000) != 0)
            {
                return;
            }

            socket.Send(WaylandWire.Message(offer, 0, writer => writer.Text(PlainMime)), pipes[1]);
            Native.close(pipes[1]);
            var read = pipes[0];
            _ = Task.Run(() =>
            {
                try
                {
                    PlainText?.Invoke(ReadAll(read));
                }
                finally
                {
                    Native.close(read);
                }
            });
        }
        else if (sources.ContainsKey(message.ObjectId) && message.Opcode == 0)
        {
            var file = socket.TakeFile();
            if (file < 0 || !sources.TryGetValue(message.ObjectId, out var text))
            {
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var written = 0;
                while (written < bytes.Length)
                {
                    var slice = bytes[written..];
                    var count = Native.write(file, slice, (nuint)slice.Length);
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
    }

    static string ReadAll(int fd)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var count = Native.read(fd, buffer, (nuint)buffer.Length);
            if (count <= 0)
            {
                break;
            }

            stream.Write(buffer, 0, (int)count);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
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

    void BindDevice()
    {
        if (seat == 0 || manager == 0 || device != 0)
        {
            return;
        }

        device = nextId++;
        socket.Send(WaylandWire.Message(manager, 1, writer =>
        {
            writer.U32(device);
            writer.U32(seat);
        }));
        syncCallback = nextId++;
        socket.Send(WaylandWire.Message(1, 0, writer => writer.U32(syncCallback)));
    }
}
