using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextAll.Host;

public sealed class WaylandMessage
{
    public required uint ObjectId { get; init; }

    public required ushort Opcode { get; init; }

    public required byte[] Body { get; init; }
}

public static class WaylandWire
{
    public static byte[] Message(uint objectId, ushort opcode, Action<WaylandWriter>? write = null)
    {
        var body = new WaylandWriter();
        write?.Invoke(body);
        var payload = body.ToArray();
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, objectId);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)bytes.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    public static bool TryRead(byte[] buffer, int length, ref int offset, out WaylandMessage? message)
    {
        message = null;
        if (length - offset < 8)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 6));
        if (size < 8 || length - offset < size)
        {
            return false;
        }

        var body = new byte[size - 8];
        Buffer.BlockCopy(buffer, offset + 8, body, 0, body.Length);
        message = new WaylandMessage
        {
            ObjectId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset)),
            Opcode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4)),
            Body = body,
        };
        offset += size;
        return true;
    }

    public static uint U32(byte[] body, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset));
        offset += 4;
        return value;
    }

    public static string Text(byte[] body, ref int offset)
    {
        var length = (int)U32(body, ref offset);
        if (length <= 0)
        {
            return "";
        }

        var text = Encoding.UTF8.GetString(body, offset, Math.Max(0, length - 1));
        offset += (length + 3) & ~3;
        return text;
    }
}

public sealed class WaylandWriter
{
    readonly List<byte> bytes = [];

    public void U32(uint value)
    {
        var raw = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
        bytes.AddRange(raw);
    }

    public void Text(string value)
    {
        var raw = Encoding.UTF8.GetBytes(value);
        var length = raw.Length + 1;
        U32((uint)length);
        bytes.AddRange(raw);
        bytes.Add(0);
        while (bytes.Count % 4 != 0)
        {
            bytes.Add(0);
        }
    }

    public byte[] ToArray() => bytes.ToArray();
}

public sealed class WaylandSocket : IDisposable
{
    readonly Socket socket;
    readonly Queue<int> files = new();
    readonly byte[] pending = new byte[65536];
    int pendingLength;
    bool disposed;

    WaylandSocket(Socket socket)
    {
        this.socket = socket;
    }

    public static WaylandSocket Connect(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(path));
        return new WaylandSocket(socket);
    }

    public static WaylandSocket Listen(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        socket.Listen(8);
        return new WaylandSocket(socket);
    }

    public WaylandSocket Accept()
    {
        var accepted = socket.Accept();
        return new WaylandSocket(accepted);
    }

    public int TakeFile() => files.Count == 0 ? -1 : files.Dequeue();

    public void Send(byte[] message, int file = -1)
    {
        var fd = (int)socket.Handle;
        var pin = GCHandle.Alloc(message, GCHandleType.Pinned);
        var vectors = new IoVec[1];
        var vectorPin = GCHandle.Alloc(vectors, GCHandleType.Pinned);
        byte[]? control = null;
        GCHandle controlPin = default;
        try
        {
            vectors[0].Base = pin.AddrOfPinnedObject();
            vectors[0].Length = (nuint)message.Length;
            var header = new MsgHdr
            {
                Iov = vectorPin.AddrOfPinnedObject(),
                IovLen = 1,
            };
            if (file >= 0)
            {
                control = Control(file);
                controlPin = GCHandle.Alloc(control, GCHandleType.Pinned);
                header.Control = controlPin.AddrOfPinnedObject();
                header.ControlLen = (nuint)control.Length;
            }

            var sent = Native.sendmsg(fd, ref header, 0x4000);
            if (sent < 0)
            {
                throw new IOException("wayland-send");
            }
        }
        finally
        {
            if (control is not null)
            {
                controlPin.Free();
            }

            vectorPin.Free();
            pin.Free();
        }
    }

    public WaylandMessage? Receive()
    {
        while (true)
        {
            var offset = 0;
            if (WaylandWire.TryRead(pending, pendingLength, ref offset, out var existing))
            {
                Compact(offset);
                return existing;
            }

            var chunk = new byte[8192];
            var control = new byte[256];
            var chunkPin = GCHandle.Alloc(chunk, GCHandleType.Pinned);
            var controlPin = GCHandle.Alloc(control, GCHandleType.Pinned);
            var vectors = new IoVec[1];
            var vectorPin = GCHandle.Alloc(vectors, GCHandleType.Pinned);
            try
            {
                vectors[0].Base = chunkPin.AddrOfPinnedObject();
                vectors[0].Length = (nuint)chunk.Length;
                var header = new MsgHdr
                {
                    Iov = vectorPin.AddrOfPinnedObject(),
                    IovLen = 1,
                    Control = controlPin.AddrOfPinnedObject(),
                    ControlLen = (nuint)control.Length,
                };
                var read = Native.recvmsg((int)socket.Handle, ref header, 0);
                if (read <= 0)
                {
                    return null;
                }

                RememberFiles(control, (int)header.ControlLen);
                if (pendingLength + (int)read > pending.Length)
                {
                    throw new IOException("wayland-buffer");
                }

                Buffer.BlockCopy(chunk, 0, pending, pendingLength, (int)read);
                pendingLength += (int)read;
            }
            finally
            {
                vectorPin.Free();
                controlPin.Free();
                chunkPin.Free();
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        socket.Dispose();
        while (files.Count > 0)
        {
            Native.close(files.Dequeue());
        }
    }

    void Compact(int offset)
    {
        Buffer.BlockCopy(pending, offset, pending, 0, pendingLength - offset);
        pendingLength -= offset;
    }

    void RememberFiles(byte[] control, int length)
    {
        var offset = 0;
        while (offset + 16 <= length)
        {
            var size = (int)BitConverter.ToUInt64(control, offset);
            if (size < 16)
            {
                return;
            }

            var level = BitConverter.ToInt32(control, offset + 8);
            var type = BitConverter.ToInt32(control, offset + 12);
            if (level == 1 && type == 1 && offset + 20 <= length)
            {
                files.Enqueue(BitConverter.ToInt32(control, offset + 16));
            }

            offset += (size + 7) & ~7;
        }
    }

    static byte[] Control(int file)
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, 20);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), file);
        return bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoVec
    {
        public nint Base;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    struct MsgHdr
    {
        [FieldOffset(0)] public nint Name;
        [FieldOffset(8)] public uint NameLen;
        [FieldOffset(16)] public nint Iov;
        [FieldOffset(24)] public nuint IovLen;
        [FieldOffset(32)] public nint Control;
        [FieldOffset(40)] public nuint ControlLen;
        [FieldOffset(48)] public int Flags;
    }

    static class Native
    {
        [DllImport("libc", SetLastError = true)]
        public static extern nint sendmsg(int fd, ref MsgHdr message, int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern nint recvmsg(int fd, ref MsgHdr message, int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);
    }
}
