using System.IO.Pipes;
using System.Net.Sockets;
using ContextAll.Core;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.Host;

public static class DaemonProcess
{
    public static int Starts { get; private set; }

    public static async Task<int> Serve(string? runtimeDirectory, Func<CancellationToken, Task>? onListening, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(runtimeDirectory) || (File.Exists(runtimeDirectory) && !Directory.Exists(runtimeDirectory)))
        {
            return 1;
        }

        Directory.CreateDirectory(runtimeDirectory);
        IdentityStore.SetDirectoryMode(runtimeDirectory);
        var directory = Path.Combine(runtimeDirectory, "contextall");
        Directory.CreateDirectory(directory);
        IdentityStore.SetDirectoryMode(directory);
        var socketPath = LocalChannelClient.LinuxSocket(runtimeDirectory);
        if (Probe(socketPath))
        {
            return 1;
        }

        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(8);
        IdentityStore.SetFileMode(socketPath);
        Starts++;
        ServiceNotify.Ready();
        var desk = new LocalDesk();
        var accepting = AcceptLoop(listener, socketPath, desk, cancellationToken);
        await WaitForStop(onListening, cancellationToken).ConfigureAwait(false);
        try
        {
            await accepting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the daemon.
        }

        return 0;
    }

    public static async Task<int> ServePipe(string userSid, Func<CancellationToken, Task>? onListening, CancellationToken cancellationToken)
    {
        NamedPipeServerStream listener;
        try
        {
            listener = new NamedPipeServerStream(
                PipeName(userSid),
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }
        catch (IOException)
        {
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            return 1;
        }

        using (listener)
        {
            Starts++;
            ServiceNotify.Ready();
            var desk = new LocalDesk();
            var accepting = AcceptPipes(listener, desk, cancellationToken);
            await WaitForStop(onListening, cancellationToken).ConfigureAwait(false);
            try
            {
                await accepting.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller stopped the daemon.
            }
        }

        return 0;
    }

    public static string PipeName(string userSid) => @"ContextAll\" + userSid;

    static async Task WaitForStop(Func<CancellationToken, Task>? onListening, CancellationToken cancellationToken)
    {
        if (onListening is null)
        {
            return;
        }

        try
        {
            await onListening(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the daemon.
        }
    }

    static async Task AcceptLoop(Socket listener, string socketPath, LocalDesk desk, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                await Pump(new NetworkStream(accepted, ownsSocket: true), desk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the daemon.
        }
        finally
        {
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }
        }
    }

    static async Task AcceptPipes(NamedPipeServerStream listener, LocalDesk desk, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await listener.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await Pump(listener, desk, cancellationToken).ConfigureAwait(false);
                if (listener.IsConnected)
                {
                    listener.Disconnect();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the daemon.
        }
        catch (IOException)
        {
            // The front end left.
        }
    }

    static async Task Pump(Stream stream, LocalDesk desk, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = desk.SnapshotFrame();
            var hello = FrameCodec.Encode(snapshot.Payload, snapshot.Type, snapshot.Priority, snapshot.Flags, snapshot.StreamId);
            if (hello.Value is null)
            {
                return;
            }

            await stream.WriteAsync(hello.Value, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes];
            var filled = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var readCount = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
                if (readCount == 0)
                {
                    return;
                }

                filled += readCount;
                while (true)
                {
                    var read = FrameCodec.Read(buffer.AsSpan(0, filled));
                    if (read.NeedMore)
                    {
                        break;
                    }

                    if (read.IsFailure || read.Frame is null)
                    {
                        return;
                    }

                    if (read.Frame.Type is >= FrameTypes.StartPairing and <= FrameTypes.CancelTransfer)
                    {
                        Frame result;
                        lock (desk)
                        {
                            result = desk.Handle(read.Frame);
                        }

                        var encoded = FrameCodec.Encode(result.Payload, result.Type, result.Priority, result.Flags, result.StreamId);
                        if (encoded.Value is not null)
                        {
                            await stream.WriteAsync(encoded.Value, cancellationToken).ConfigureAwait(false);
                            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    var rest = filled - read.Consumed;
                    Buffer.BlockCopy(buffer, read.Consumed, buffer, 0, rest);
                    filled = rest;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the daemon.
        }
        catch (IOException)
        {
            // The front end left.
        }
    }

    public static bool Probe(string socketPath)
    {
        if (!File.Exists(socketPath))
        {
            return false;
        }

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            client.Connect(new UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
