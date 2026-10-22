using System.Net.Sockets;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.Host;

public static class LocalClient
{
    public static int DeviceList(CliIo io)
    {
        if (!TryOpen(io, out var stream))
        {
            return 1;
        }

        using (stream)
        {
            var snapshot = Read(stream);
            var payload = ControlJson.Decode(snapshot.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value;
            if (payload is null)
            {
                io.Error.WriteLine("The daemon is not running.");
                return 1;
            }

            foreach (var device in payload.Devices)
            {
                io.Out.WriteLine($"{device.DeviceId}\t{device.DisplayName}\t{(device.Trusted ? "trusted" : "new")}\t{device.Presence}");
            }

            return 0;
        }
    }

    public static int Pair(CliIo io, string deviceId) =>
        Command(io, FrameTypes.StartPairing, new LocalCommandPayload(RequestId(), deviceId, null, null, null, null));

    public static int Clipboard(CliIo io, string deviceId, string flag) =>
        Switch(io, FrameTypes.SetClipboardSync, deviceId, flag);

    public static int AskBeforeSave(CliIo io, string deviceId, string flag) =>
        Switch(io, FrameTypes.SetAskBeforeSave, deviceId, flag);

    public static int SendFile(CliIo io, string deviceId, string path) =>
        Command(io, FrameTypes.SendFile, new LocalCommandPayload(RequestId(), deviceId, null, path, null, null));

    static int Switch(CliIo io, byte type, string deviceId, string flag)
    {
        var enabled = flag switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => (bool?)null,
        };
        if (enabled is null)
        {
            io.Error.WriteLine("Usage: contextall <command>");
            return 1;
        }

        return Command(io, type, new LocalCommandPayload(RequestId(), deviceId, null, null, enabled, null));
    }

    static int Command(CliIo io, byte type, LocalCommandPayload payload)
    {
        if (!TryOpen(io, out var stream))
        {
            return 1;
        }

        using (stream)
        {
            _ = Read(stream);
            var encoded = ControlJson.Encode(payload, ProtocolJsonContext.Default.LocalCommandPayload);
            var frame = FrameCodec.Encode(encoded.Value ?? [], type, 0, 0, 0).Value;
            if (frame is null)
            {
                io.Error.WriteLine("invalid");
                return 1;
            }

            stream.Write(frame);
            stream.Flush();
            while (true)
            {
                var reply = Read(stream);
                if (reply.Type != FrameTypes.CommandResult)
                {
                    continue;
                }

                var result = ControlJson.Decode(reply.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value;
                if (result is null || !result.Ok)
                {
                    io.Error.WriteLine(result?.Error ?? "invalid");
                    return 1;
                }

                return 0;
            }
        }
    }

    static bool TryOpen(CliIo io, out NetworkStream stream)
    {
        stream = null!;
        if (string.IsNullOrEmpty(io.RuntimeDirectory))
        {
            io.Error.WriteLine("The daemon is not running.");
            return false;
        }

        var socketPath = LocalChannelClient.LinuxSocket(io.RuntimeDirectory);
        if (!DaemonProcess.Probe(socketPath))
        {
            io.Error.WriteLine("The daemon is not running.");
            return false;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.ReceiveTimeout = 3000;
        socket.SendTimeout = 3000;
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        stream = new NetworkStream(socket, ownsSocket: true);
        return true;
    }

    static Frame Read(NetworkStream stream)
    {
        var buffer = new byte[FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var count = stream.Read(buffer, filled, buffer.Length - filled);
            if (count == 0)
            {
                break;
            }

            filled += count;
            var read = FrameCodec.Read(buffer.AsSpan(0, filled));
            if (read.Frame is not null)
            {
                return read.Frame;
            }

            if (read.IsFailure)
            {
                throw new IOException(read.Reason);
            }
        }

        throw new IOException("no frame");
    }

    static string RequestId() => Guid.NewGuid().ToString("N")[..16];
}
