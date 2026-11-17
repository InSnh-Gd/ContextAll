using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using ContextAll.Protocol;

namespace ContextAll.Ipc;

public interface ILocalDaemonClient : IAsyncDisposable
{
    LocalShellState CurrentState { get; }

    event Action<LocalShellState>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    void RetryNow();

    Task<CommandResultPayload> SendCommandAsync(byte frameType, LocalCommandPayload payload, CancellationToken cancellationToken = default);
}

public static class LocalDaemonClientExtensions
{
    public const int InlineClipboardThresholdBytes = 16 * 1024;
    public const int ClipboardChunkSizeBytes = 32 * 1024;

    public static string NewRequestId() => Guid.NewGuid().ToString("N")[..16];

    public static Task<CommandResultPayload> SetDisplayNameAsync(this ILocalDaemonClient client, string displayName, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.SetDisplayName, new LocalCommandPayload(NewRequestId(), null, displayName, null, null, null), ct);

    public static Task<CommandResultPayload> SetDownloadDirectoryAsync(this ILocalDaemonClient client, string path, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.SetDownloadDirectory, new LocalCommandPayload(NewRequestId(), null, null, path, null, null), ct);

    public static Task<CommandResultPayload> StartPairingAsync(this ILocalDaemonClient client, string deviceId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.StartPairing, new LocalCommandPayload(NewRequestId(), deviceId, null, null, null, null), ct);

    public static Task<CommandResultPayload> AcceptPairingAsync(this ILocalDaemonClient client, string deviceId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.AcceptPairing, new LocalCommandPayload(NewRequestId(), deviceId, null, null, null, null), ct);

    public static Task<CommandResultPayload> RejectPairingAsync(this ILocalDaemonClient client, string deviceId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.RejectPairing, new LocalCommandPayload(NewRequestId(), deviceId, null, null, null, null), ct);

    public static Task<CommandResultPayload> UnpairAsync(this ILocalDaemonClient client, string deviceId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.UnpairLocal, new LocalCommandPayload(NewRequestId(), deviceId, null, null, null, null), ct);

    public static Task<CommandResultPayload> SetClipboardSyncAsync(this ILocalDaemonClient client, string deviceId, bool enabled, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.SetClipboardSync, new LocalCommandPayload(NewRequestId(), deviceId, null, null, enabled, null), ct);

    public static Task<CommandResultPayload> SetAskBeforeSaveAsync(this ILocalDaemonClient client, string deviceId, bool enabled, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.SetAskBeforeSave, new LocalCommandPayload(NewRequestId(), deviceId, null, null, enabled, null), ct);

    public static Task<CommandResultPayload> SendFileAsync(this ILocalDaemonClient client, string deviceId, string path, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.SendFile, new LocalCommandPayload(NewRequestId(), deviceId, null, path, null, null), ct);

    public static Task<CommandResultPayload> AcceptFileAsync(this ILocalDaemonClient client, string transferId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.AcceptFile, new LocalCommandPayload(NewRequestId(), null, null, null, null, transferId), ct);

    public static Task<CommandResultPayload> RejectFileAsync(this ILocalDaemonClient client, string transferId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.RejectFile, new LocalCommandPayload(NewRequestId(), null, null, null, null, transferId), ct);

    public static Task<CommandResultPayload> CancelTransferAsync(this ILocalDaemonClient client, string transferId, CancellationToken ct = default) =>
        client.SendCommandAsync(FrameTypes.CancelTransfer, new LocalCommandPayload(NewRequestId(), null, null, null, null, transferId), ct);

    public static Task<CommandResultPayload> ConnectEndpointAsync(this ILocalDaemonClient client, string host, int port, string? alias = null, CancellationToken ct = default) =>
        client.SendCommandAsync(
            FrameTypes.ConnectEndpoint,
            new LocalCommandPayload(NewRequestId(), null, null, null, null, null, Host: host, Port: port, Alias: string.IsNullOrWhiteSpace(alias) ? null : alias.Trim()),
            ct);

    public static async Task<CommandResultPayload> PushClipboardTextAsync(this ILocalDaemonClient client, string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(text);

        var utf8 = Encoding.UTF8.GetBytes(text);
        if (utf8.Length > FrameLimits.MaxClipboardBytes)
        {
            return await client.SendCommandAsync(
                FrameTypes.PushClipboardText,
                new LocalCommandPayload(NewRequestId(), null, null, null, null, null, TotalBytes: utf8.Length),
                ct).ConfigureAwait(false);
        }

        if (utf8.Length <= InlineClipboardThresholdBytes)
        {
            return await client.SendCommandAsync(
                FrameTypes.PushClipboardText,
                new LocalCommandPayload(NewRequestId(), null, null, null, null, null, Text: text),
                ct).ConfigureAwait(false);
        }

        CommandResultPayload last = new(NewRequestId(), false, "invalid");
        for (var offset = 0; offset < utf8.Length; offset += ClipboardChunkSizeBytes)
        {
            var count = Math.Min(ClipboardChunkSizeBytes, utf8.Length - offset);
            var chunk = new LocalCommandPayload(
                NewRequestId(),
                null,
                null,
                null,
                null,
                null,
                TotalBytes: utf8.Length,
                Offset: offset,
                ChunkBase64: Convert.ToBase64String(utf8, offset, count));
            last = await client.SendCommandAsync(FrameTypes.PushClipboardText, chunk, ct).ConfigureAwait(false);
            if (!last.Ok)
            {
                return last;
            }
        }

        return last;
    }
}

public sealed class LocalDaemonConnection : ILocalDaemonClient
{
    readonly string? runtimeDirectory;
    readonly string? explicitSocketPath;
    readonly string? explicitPipeName;
    readonly TimeSpan initialReconnectDelay;
    readonly TimeSpan maxReconnectDelay;
    readonly Func<DateTimeOffset> clock;
    readonly object stateGate = new();
    readonly SemaphoreSlim writeGate = new(1, 1);
    readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResultPayload>> pending = new();
    readonly CancellationTokenSource disposeCts = new();

    CancellationTokenSource? delayCts;
    Stream? activeStream;
    Task? loopTask;
    int started;
    bool disposed;
    LocalShellState state = LocalShellState.Empty;

    public LocalDaemonConnection(
        string? runtimeDirectory = null,
        string? explicitSocketPath = null,
        string? explicitPipeName = null,
        TimeSpan? initialReconnectDelay = null,
        TimeSpan? maxReconnectDelay = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.runtimeDirectory = runtimeDirectory;
        this.explicitSocketPath = explicitSocketPath;
        this.explicitPipeName = explicitPipeName;
        this.initialReconnectDelay = initialReconnectDelay ?? TimeSpan.FromMilliseconds(250);
        this.maxReconnectDelay = maxReconnectDelay ?? TimeSpan.FromSeconds(2);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public LocalShellState CurrentState
    {
        get
        {
            lock (stateGate)
            {
                return state;
            }
        }
    }

    public event Action<LocalShellState>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token, cancellationToken);
            loopTask = Task.Run(() => RunLoopAsync(linked), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public void RetryNow()
    {
        CancellationTokenSource? toCancel;
        lock (stateGate)
        {
            toCancel = delayCts;
        }

        try
        {
            toCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Delay timer already completed.
        }
    }

    public async Task<CommandResultPayload> SendCommandAsync(byte frameType, LocalCommandPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (disposed)
        {
            return new CommandResultPayload(payload.RequestId, false, "disposed");
        }

        var encodedJson = ControlJson.Encode(payload, ProtocolJsonContext.Default.LocalCommandPayload);
        if (encodedJson.IsFailure || encodedJson.Value is null)
        {
            return new CommandResultPayload(payload.RequestId, false, encodedJson.Reason ?? "invalid");
        }

        var frame = FrameCodec.Encode(encodedJson.Value, frameType, 0, 0, FrameTypes.SessionStream);
        if (frame.IsFailure || frame.Value is null)
        {
            return new CommandResultPayload(payload.RequestId, false, frame.Reason ?? "too-large");
        }

        var stream = Volatile.Read(ref activeStream);
        if (stream is null)
        {
            return new CommandResultPayload(payload.RequestId, false, "not-connected");
        }

        var tcs = new TaskCompletionSource<CommandResultPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(payload.RequestId, tcs))
        {
            return new CommandResultPayload(payload.RequestId, false, "duplicate-request");
        }

        using var reg = cancellationToken.Register(
            static obj =>
            {
                if (obj is KeyValuePair<string, TaskCompletionSource<CommandResultPayload>> pair)
                {
                    pair.Value.TrySetResult(new CommandResultPayload(pair.Key, false, "cancelled"));
                }
            },
            new KeyValuePair<string, TaskCompletionSource<CommandResultPayload>>(payload.RequestId, tcs));

        try
        {
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var current = Volatile.Read(ref activeStream);
                if (current is null)
                {
                    return new CommandResultPayload(payload.RequestId, false, "not-connected");
                }

                await current.WriteAsync(frame.Value, cancellationToken).ConfigureAwait(false);
                await current.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new CommandResultPayload(payload.RequestId, false, "cancelled");
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            return new CommandResultPayload(payload.RequestId, false, "disconnected");
        }
        finally
        {
            pending.TryRemove(payload.RequestId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        disposeCts.Cancel();
        RetryNow();

        var stream = Interlocked.Exchange(ref activeStream, null);
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Disposed cleanly.
            }
        }

        FailAllPending("disposed");
        writeGate.Dispose();
        disposeCts.Dispose();
    }

    async Task RunLoopAsync(CancellationTokenSource linkedCts)
    {
        var token = linkedCts.Token;
        var currentDelay = initialReconnectDelay;
        try
        {
            while (!token.IsCancellationRequested)
            {
                Stream? stream = null;
                string? disconnectReason = null;
                try
                {
                    UpdateState(LocalShellReducer.OnConnecting);
                    stream = await OpenTransportAsync(token).ConfigureAwait(false);
                    if (stream is null)
                    {
                        disconnectReason = "daemon-unavailable";
                    }
                    else
                    {
                        Volatile.Write(ref activeStream, stream);
                        currentDelay = initialReconnectDelay;
                        disconnectReason = await PumpFramesAsync(stream, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or TimeoutException)
                {
                    disconnectReason = "connection-lost";
                }
                finally
                {
                    Volatile.Write(ref activeStream, null);
                    if (stream is not null)
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                    }

                    FailAllPending(disconnectReason ?? "disconnected");
                    UpdateState(s => LocalShellReducer.OnDisconnected(s, disconnectReason));
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (stateGate)
                {
                    delayCts = wake;
                }

                try
                {
                    await Task.Delay(currentDelay, wake.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // RetryNow requested.
                }
                finally
                {
                    lock (stateGate)
                    {
                        if (ReferenceEquals(delayCts, wake))
                        {
                            delayCts = null;
                        }
                    }
                }

                var doubledMs = Math.Min(maxReconnectDelay.TotalMilliseconds, currentDelay.TotalMilliseconds * 2);
                currentDelay = TimeSpan.FromMilliseconds(Math.Max(initialReconnectDelay.TotalMilliseconds, doubledMs));
            }
        }
        finally
        {
            linkedCts.Dispose();
        }
    }

    async Task<Stream?> OpenTransportAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeName = explicitPipeName;
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                pipeName = @"ContextAll\" + LocalChannelClient.CurrentUserSid();
            }

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(1000, token).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        var socketPath = explicitSocketPath;
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            var dir = runtimeDirectory ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(dir))
            {
                return null;
            }

            socketPath = LocalChannelClient.LinuxSocket(dir);
        }

        if (!File.Exists(socketPath))
        {
            return null;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    async Task<string> PumpFramesAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[(FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes) * 2];
        var filled = 0;

        while (!token.IsCancellationRequested)
        {
            if (filled == buffer.Length)
            {
                return "frame-overflow";
            }

            var read = await stream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), token).ConfigureAwait(false);
            if (read == 0)
            {
                return "eof";
            }

            filled += read;
            var offset = 0;
            while (offset < filled)
            {
                var outcome = FrameCodec.Read(buffer.AsSpan(offset, filled - offset));
                if (outcome.IsFailure)
                {
                    return outcome.Reason ?? "invalid-frame";
                }

                if (outcome.Frame is null)
                {
                    break;
                }

                offset += outcome.Consumed;
                HandleIncomingFrame(outcome.Frame);
            }

            if (offset > 0)
            {
                var remaining = filled - offset;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(buffer, offset, buffer, 0, remaining);
                }

                filled = remaining;
            }
        }

        return "cancelled";
    }

    void HandleIncomingFrame(Frame frame)
    {
        if (frame.Type == FrameTypes.CommandResult)
        {
            var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload);
            if (!decoded.IsFailure && decoded.Value is { } result && pending.TryRemove(result.RequestId, out var tcs))
            {
                tcs.TrySetResult(result);
            }

            return;
        }

        UpdateState(current => LocalShellReducer.Apply(current, frame, clock));
    }

    void UpdateState(Func<LocalShellState, LocalShellState> transform)
    {
        LocalShellState next;
        lock (stateGate)
        {
            var transformed = transform(state);
            if (ReferenceEquals(transformed, state) || Equals(transformed, state))
            {
                return;
            }

            state = transformed;
            next = transformed;
        }

        StateChanged?.Invoke(next);
    }

    void FailAllPending(string error)
    {
        foreach (var pair in pending)
        {
            if (pending.TryRemove(pair.Key, out var tcs))
            {
                tcs.TrySetResult(new CommandResultPayload(pair.Key, false, error));
            }
        }
    }
}
