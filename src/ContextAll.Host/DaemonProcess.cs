using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using ContextAll.Core;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.Host;

public static class DaemonProcess
{
    public static int Starts { get; private set; }

    public static Task<int> Serve(string? runtimeDirectory, Func<CancellationToken, Task>? onListening, CancellationToken cancellationToken) =>
        Serve(runtimeDirectory, LanHost.TryCreate, onListening, cancellationToken);

    public static Task<int> ServeDefault(string? runtimeDirectory, Func<CancellationToken, Task>? onListening, CancellationToken cancellationToken) =>
        ServeDefault(runtimeDirectory, LanHost.TryCreate, onListening, cancellationToken);

    internal static Task<int> ServeDefault(
        string? runtimeDirectory,
        Func<LanHost?> hostFactory,
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken,
        bool? windows = null,
        string? userSid = null)
    {
        var server = ChannelServers.ForPlatform(
            windows ?? OperatingSystem.IsWindows(),
            runtimeDirectory,
            userSid ?? LocalChannelClient.CurrentUserSid());
        return server.ServeAsync(hostFactory, onListening, cancellationToken);
    }

    internal static async Task<int> Serve(
        string? runtimeDirectory,
        Func<LanHost?> hostFactory,
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken)
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

        var host = hostFactory();
        if (host is null)
        {
            return 1;
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(8);
            IdentityStore.SetFileMode(socketPath);
            Starts++;
            host.Start(cancellationToken);
            ServiceNotify.Ready();
            var accepting = AcceptLoop(listener, socketPath, host, cancellationToken);
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
        finally
        {
            host.Dispose();
        }

        return 0;
    }

    public static Task<int> ServePipe(string userSid, Func<CancellationToken, Task>? onListening, CancellationToken cancellationToken) =>
        ServePipe(userSid, LanHost.TryCreate, onListening, cancellationToken);

    internal static async Task<int> ServePipe(
        string userSid,
        Func<LanHost?> hostFactory,
        Func<CancellationToken, Task>? onListening,
        CancellationToken cancellationToken)
    {
        using var claimed = PipeInstance.TryClaim(userSid);
        if (claimed is null)
        {
            return 1;
        }

        var host = hostFactory();
        if (host is null)
        {
            return 1;
        }

        try
        {
            Starts++;
            host.Start(cancellationToken);
            ServiceNotify.Ready();
            var accepting = AcceptPipes(userSid, host, cancellationToken);
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
        finally
        {
            host.Dispose();
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

    static async Task AcceptLoop(Socket listener, string socketPath, LanHost host, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var accepted = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                _ = PumpOwned(new NetworkStream(accepted, ownsSocket: true), host.Snapshot, host.Command, host.Join, host.Leave, cancellationToken);
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

    static async Task AcceptPipes(string userSid, LanHost host, CancellationToken cancellationToken)
    {
        var pipeName = PipeName(userSid);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream listener;
                try
                {
                    listener = CreatePipeListener(pipeName, userSid);
                }
                catch (IOException)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }

                try
                {
                    await listener.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await listener.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                catch (IOException)
                {
                    await listener.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                _ = PumpOwned(listener, host.Snapshot, host.Command, host.Join, host.Leave, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The caller stopped the daemon.
        }
    }

    static NamedPipeServerStream CreatePipeListener(string pipeName, string userSid)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsPipe(pipeName, userSid);
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    [SupportedOSPlatform("windows")]
    static NamedPipeServerStream CreateWindowsPipe(string pipeName, string userSid)
    {
        var security = PipeAcl.CurrentUserOnly(userSid);
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    static async Task PumpOwned(
        Stream stream,
        Func<Frame> snapshot,
        Action<FrontClient, Frame> command,
        Action<FrontClient> join,
        Action<FrontClient> leave,
        CancellationToken cancellationToken)
    {
        try
        {
            await Pump(stream, snapshot, command, join, leave, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    static async Task Pump(
        Stream stream,
        Func<Frame> snapshot,
        Action<FrontClient, Frame> command,
        Action<FrontClient> join,
        Action<FrontClient> leave,
        CancellationToken cancellationToken)
    {
        var client = new FrontClient();
        var writing = client.CopyTo(stream, cancellationToken);
        try
        {
            client.Post(snapshot());
            join(client);
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

                    if (read.IsFailure || read.Frame is null || !LocalFrameTypes.IsCommand(read.Frame.Type))
                    {
                        return;
                    }

                    command(client, read.Frame);
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
        finally
        {
            leave(client);
            client.Complete();
            try
            {
                await writing.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller stopped the daemon.
            }
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
