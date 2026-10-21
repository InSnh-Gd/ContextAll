using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using ContextAll.Host;

namespace ContextAll.Host.Tests;

public sealed class WlrCompositor : IDisposable
{
    readonly object gate = new();
    readonly WaylandSocket listener;
    readonly Dictionary<uint, string> pushed = [];
    readonly Thread thread;
    WaylandSocket? client;
    uint registry;
    uint device;
    bool running = true;

    public WlrCompositor(string path)
    {
        Path = path;
        listener = WaylandSocket.Listen(path);
        thread = new Thread(Loop) { IsBackground = true };
        thread.Start();
    }

    public string Path { get; }

    public int Bound { get; private set; }

    public string CurrentText { get; private set; } = "";

    public int Revisions { get; private set; }

    public void SetText(string text)
    {
        lock (gate)
        {
            if (client is null || device == 0)
            {
                return;
            }

            if (text.Length == 0)
            {
                client.Send(WaylandWire.Message(device, 1, writer => writer.U32(0)));
                return;
            }

            var offer = 0xff000001u + (uint)Revisions;
            pushed[offer] = text;
            client.Send(WaylandWire.Message(device, 0, writer => writer.U32(offer)));
            client.Send(WaylandWire.Message(offer, 0, writer => writer.Text(WaylandClipboard.PlainMime)));
            client.Send(WaylandWire.Message(device, 1, writer => writer.U32(offer)));
        }
    }

    public void Dispose()
    {
        running = false;
        listener.Dispose();
        lock (gate)
        {
            client?.Dispose();
        }
    }

    void Loop()
    {
        while (running)
        {
            WaylandSocket accepted;
            try
            {
                accepted = listener.Accept();
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (gate)
            {
                client = accepted;
                registry = 0;
                device = 0;
            }

            try
            {
                while (running)
                {
                    var message = accepted.Receive();
                    if (message is null)
                    {
                        break;
                    }

                    Handle(accepted, message);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // The client left.
            }

            lock (gate)
            {
                if (ReferenceEquals(client, accepted))
                {
                    client = null;
                    device = 0;
                }
            }
        }
    }

    void Handle(WaylandSocket socket, WaylandMessage message)
    {
        if (message.ObjectId == 1 && message.Opcode == 1)
        {
            var offset = 0;
            registry = WaylandWire.U32(message.Body, ref offset);
            lock (gate)
            {
                socket.Send(WaylandWire.Message(registry, 0, writer =>
                {
                    writer.U32(1);
                    writer.Text("wl_seat");
                    writer.U32(1);
                }));
                socket.Send(WaylandWire.Message(registry, 0, writer =>
                {
                    writer.U32(2);
                    writer.Text("zwlr_data_control_manager_v1");
                    writer.U32(1);
                }));
            }

            return;
        }

        if (message.ObjectId == 1 && message.Opcode == 0)
        {
            var offset = 0;
            var callback = WaylandWire.U32(message.Body, ref offset);
            lock (gate)
            {
                socket.Send(WaylandWire.Message(callback, 0, writer => writer.U32(0)));
            }

            return;
        }

        if (message.ObjectId == registry && message.Opcode == 0)
        {
            return;
        }

        if (message.Opcode == 1 && message.Body.Length >= 8)
        {
            var offset = 0;
            var id = WaylandWire.U32(message.Body, ref offset);
            var seat = WaylandWire.U32(message.Body, ref offset);
            if (seat != 0)
            {
                device = id;
                Bound++;
            }

            return;
        }

        if (message.Opcode == 0 && message.ObjectId == device && message.Body.Length >= 4)
        {
            var offset = 0;
            var source = WaylandWire.U32(message.Body, ref offset);
            ReadSource(socket, source);
            return;
        }

        if (pushed.ContainsKey(message.ObjectId) && message.Opcode == 0)
        {
            var file = socket.TakeFile();
            if (file < 0 || !pushed.TryGetValue(message.ObjectId, out var text))
            {
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                if (bytes.Length > 0)
                {
                    Native.write(file, bytes, (nuint)bytes.Length);
                }
            }
            finally
            {
                Native.close(file);
            }
        }
    }

    void ReadSource(WaylandSocket socket, uint source)
    {
        var pipes = new int[2];
        if (Native.pipe2(pipes, 0x80000) != 0)
        {
            return;
        }

        lock (gate)
        {
            socket.Send(WaylandWire.Message(source, 0, writer => writer.Text(WaylandClipboard.PlainMime)), pipes[1]);
        }

        Native.close(pipes[1]);
        var text = ReadAll(pipes[0]);
        Native.close(pipes[0]);
        CurrentText = text;
        Revisions++;
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
}
