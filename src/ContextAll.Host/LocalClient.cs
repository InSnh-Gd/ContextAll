using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
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

    public static int Connect(CliIo io, string endpoint, string? alias = null)
    {
        if (!TryParseEndpoint(endpoint, out var host, out var port))
        {
            io.Error.WriteLine("invalid-endpoint");
            return 1;
        }

        var trimmedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        return Command(
            io,
            FrameTypes.ConnectEndpoint,
            new LocalCommandPayload(RequestId(), null, null, null, null, null, Host: host, Port: port, Alias: trimmedAlias),
            timeoutMs: 10_000);
    }

    public static bool TryParseEndpoint(string raw, out string host, out int port)
    {
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        string hostPart;
        string portPart;
        if (trimmed.StartsWith('['))
        {
            var closeBracket = trimmed.IndexOf(']');
            if (closeBracket <= 1 || closeBracket + 2 > trimmed.Length || trimmed[closeBracket + 1] != ':')
            {
                return false;
            }

            hostPart = trimmed[1..closeBracket];
            portPart = trimmed[(closeBracket + 2)..];
        }
        else
        {
            var lastColon = trimmed.LastIndexOf(':');
            if (lastColon <= 0 || lastColon == trimmed.Length - 1)
            {
                return false;
            }

            if (trimmed.IndexOf(':') != lastColon)
            {
                return false;
            }

            hostPart = trimmed[..lastColon];
            portPart = trimmed[(lastColon + 1)..];
        }

        if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
        {
            return false;
        }

        host = hostPart.Trim();
        if (host.Length == 0 || host.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch)))
        {
            return false;
        }

        return IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

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

    static int Command(CliIo io, byte type, LocalCommandPayload payload, int timeoutMs = 3000)
    {
        if (!TryOpen(io, out var stream, timeoutMs))
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
                if (result is null || result.RequestId != payload.RequestId)
                {
                    continue;
                }

                if (!result.Ok)
                {
                    io.Error.WriteLine(result.Error ?? "invalid");
                    return 1;
                }

                return 0;
            }
        }
    }

    static bool TryOpen(CliIo io, [NotNullWhen(true)] out Stream? stream, int timeoutMs = 3000)
    {
        stream = null;
        if (OperatingSystem.IsWindows())
        {
            return TryOpenPipe(io, out stream, timeoutMs);
        }

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
        socket.ReceiveTimeout = timeoutMs;
        socket.SendTimeout = timeoutMs;
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        stream = new NetworkStream(socket, ownsSocket: true);
        return true;
    }

    static bool TryOpenPipe(CliIo io, [NotNullWhen(true)] out Stream? stream, int timeoutMs)
    {
        stream = null;
        var pipe = new NamedPipeClientStream(".", DaemonProcess.PipeName(LocalChannelClient.CurrentUserSid()), PipeDirection.InOut);
        try
        {
            pipe.Connect(timeoutMs);
            stream = pipe;
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            io.Error.WriteLine("The daemon is not running.");
            return false;
        }
    }

    static Frame Read(Stream stream)
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
