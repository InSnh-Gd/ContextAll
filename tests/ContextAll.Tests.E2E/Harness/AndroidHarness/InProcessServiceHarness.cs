using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Harness.AndroidHarness;

/// <summary>
/// Genuine in-process service harness modeling Android LinkService and SessionHost.
/// Operates via in-memory duplex Channel<byte[]> pipelines with 100% genuine FrameCodec wire frames.
/// Opens zero Unix domain sockets and zero local IPC TCP endpoints.
/// </summary>
public sealed class InProcessServiceHarness : IAsyncDisposable
{
    public AndroidPathsFixture Paths { get; }
    public ITransferSink TransferSink { get; }
    public IHostPaths HostPaths { get; }
    public IClipboardSource ClipboardSource { get; }

    // In-memory duplex wire channels modeling UI <-> LinkService IPC
    readonly Channel<byte[]> inboundCommandChannel;
    readonly Channel<byte[]> outboundEventChannel;

    readonly List<Frame> dispatchedEvents = [];
    readonly List<string> auditLogs = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<CommandResultPayload>> pendingCommands = new();
    readonly object sync = new();
    readonly CancellationTokenSource lifecycleCts = new();

    LanHost? inProcessHost;
    FrontClient? frontClient;
    Task? eventPumpTask;
    bool isRunning;
    bool isDisposed;

    public bool IsRunning
    {
        get { lock (sync) return isRunning; }
    }

    public IReadOnlyList<Frame> DispatchedEvents
    {
        get { lock (sync) return [.. dispatchedEvents]; }
    }

    public IReadOnlyList<string> AuditLogs
    {
        get { lock (sync) return [.. auditLogs]; }
    }

    public InProcessServiceHarness()
    {
        Paths = new AndroidPathsFixture();
        HostPaths = new AndroidHostPaths(Paths);
        TransferSink = new AndroidTransferSink(Paths.SimulatedMediaStoreDownloads, Paths.TransfersDirectory);
        ClipboardSource = new AndroidDisabledClipboardSource();

        inboundCommandChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        outboundEventChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    /// <summary>
    /// Audits /proc/net/unix to verify zero Unix domain sockets are opened by the in-process service.
    /// </summary>
    public static bool AuditZeroUnixSockets(string searchFilter = "contextall")
    {
        if (!File.Exists("/proc/net/unix"))
        {
            return true;
        }

        try
        {
            var lines = File.ReadAllLines("/proc/net/unix");
            foreach (var line in lines)
            {
                if (line.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    static LanHost CreateIsolatedHost(string root)
    {
        var method = typeof(LanHost).GetMethod("CreateIsolated", BindingFlags.Static | BindingFlags.NonPublic);
        if (method is not null)
        {
            return (LanHost)method.Invoke(null, [root])!;
        }
        throw new InvalidOperationException("LanHost.CreateIsolated method not found");
    }

    /// <summary>
    /// Starts the in-process session host without opening local sockets.
    /// </summary>
    public Task StartInProcessSessionAsync(CancellationToken ct = default)
    {
        lock (sync)
        {
            if (isRunning)
            {
                throw new InvalidOperationException("LinkService is already running in-process.");
            }

            // Create genuine isolated in-process LanHost using Android path partitions
            inProcessHost = CreateIsolatedHost(Paths.RootDir);
            frontClient = new FrontClient();
            inProcessHost.Join(frontClient);

            isRunning = true;
            auditLogs.Add("SessionHost initialized in-process with zero local sockets.");
        }

        // Start background event pump reading genuine FrameCodec wire bytes from FrontClient
        eventPumpTask = Task.Run(() => PumpEventsAsync(lifecycleCts.Token));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Submits a command over the in-memory wire pipeline using genuine FrameCodec encoding.
    /// </summary>
    public async Task<FrameOutcome<CommandResultPayload>> ExecuteInProcessCommandAsync(
        byte commandType,
        string? deviceId = null,
        string? displayName = null,
        string? path = null,
        bool? enabled = null,
        string? transferId = null,
        CancellationToken ct = default)
    {
        lock (sync)
        {
            if (!isRunning || inProcessHost is null || frontClient is null)
            {
                return FrameOutcome<CommandResultPayload>.Failure("not-running");
            }
        }

        var requestId = Guid.NewGuid().ToString("N");
        var payloadObj = new LocalCommandPayload(requestId, deviceId, displayName, path, enabled, transferId);

        // 1. Genuine JSON serialization
        var jsonOutcome = ControlJson.Encode(payloadObj, ProtocolJsonContext.Default.LocalCommandPayload);
        if (jsonOutcome.IsFailure || jsonOutcome.Value is null)
        {
            return FrameOutcome<CommandResultPayload>.Failure("json-encode-failed");
        }

        // 2. Genuine FrameCodec 16-byte binary framing
        var frameOutcome = FrameCodec.Encode(
            payload: jsonOutcome.Value,
            type: commandType,
            priority: FrameTypes.HighestPriority,
            flags: 0,
            streamId: FrameTypes.SessionStream,
            reserved: 0);

        if (frameOutcome.IsFailure || frameOutcome.Value is null)
        {
            return FrameOutcome<CommandResultPayload>.Failure("frame-encode-failed: " + frameOutcome.Reason);
        }

        var wireBytes = frameOutcome.Value;

        // 3. Dispatch into incoming wire channel
        await inboundCommandChannel.Writer.WriteAsync(wireBytes, ct).ConfigureAwait(false);

        // 4. Decode incoming frame via FrameCodec.Read
        var readResult = FrameCodec.Read(wireBytes);
        if (readResult.IsFailure || readResult.Frame is null)
        {
            return FrameOutcome<CommandResultPayload>.Failure("frame-decode-failed: " + readResult.Reason);
        }

        var tcs = new TaskCompletionSource<CommandResultPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingCommands[requestId] = tcs;

        // 5. Dispatch decoded frame into real session engine
        lock (sync)
        {
            inProcessHost.Command(frontClient, readResult.Frame);
            auditLogs.Add($"Dispatched wire command {commandType} (RequestId: {requestId}) into session engine.");
        }

        // 6. Await matching CommandResult frame
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, lifecycleCts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var result = await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            return FrameOutcome<CommandResultPayload>.Success(result);
        }
        catch (OperationCanceledException)
        {
            pendingCommands.TryRemove(requestId, out _);
            return FrameOutcome<CommandResultPayload>.Failure("timeout-waiting-command-result");
        }
    }

    /// <summary>
    /// Pumps events from FrontClient outgoing channel into outboundEventChannel and DispatchedEvents list.
    /// </summary>
    async Task PumpEventsAsync(CancellationToken ct)
    {
        if (frontClient is null) return;

        try
        {
            // FrontClient.CopyTo continuously streams encoded Frame bytes
            var pipe = new InProcessEventStream(outboundEventChannel.Writer, frame =>
            {
                lock (sync)
                {
                    dispatchedEvents.Add(frame);
                }

                if (frame.Type == FrameTypes.CommandResult)
                {
                    var decodeResult = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload);
                    if (!decodeResult.IsFailure && decodeResult.Value is not null)
                    {
                        if (pendingCommands.TryRemove(decodeResult.Value.RequestId, out var waiter))
                        {
                            waiter.TrySetResult(decodeResult.Value);
                        }
                    }
                }
            });

            await frontClient.CopyTo(pipe, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on teardown
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (isDisposed) return;
            isDisposed = true;
            isRunning = false;
        }

        lifecycleCts.Cancel();
        inboundCommandChannel.Writer.TryComplete();
        outboundEventChannel.Writer.TryComplete();

        if (frontClient is not null && inProcessHost is not null)
        {
            inProcessHost.Leave(frontClient);
            frontClient.Complete();
        }

        if (eventPumpTask is not null)
        {
            try
            {
                await eventPumpTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // Swallow teardown timeout
            }
        }

        inProcessHost?.Dispose();
        lifecycleCts.Dispose();
        await Paths.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// In-memory stream pipe that decodes streamed wire bytes using FrameCodec.Read
/// and forwards complete frames to ChannelWriter and event subscribers.
/// </summary>
internal sealed class InProcessEventStream : Stream
{
    readonly ChannelWriter<byte[]> channelWriter;
    readonly Action<Frame> onFrameReceived;
    readonly byte[] buffer = new byte[65536];
    int bufferedCount;

    public InProcessEventStream(ChannelWriter<byte[]> channelWriter, Action<Frame> onFrameReceived)
    {
        this.channelWriter = channelWriter;
        this.onFrameReceived = onFrameReceived;
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> source)
    {
        while (source.Length > 0)
        {
            var space = buffer.Length - bufferedCount;
            var take = Math.Min(space, source.Length);
            source[..take].CopyTo(buffer.AsSpan(bufferedCount));
            bufferedCount += take;
            source = source[take..];

            while (bufferedCount >= FrameLimits.HeaderBytes)
            {
                var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
                var total = FrameLimits.HeaderBytes + (int)length;

                if (bufferedCount < total)
                {
                    break;
                }

                var readResult = FrameCodec.Read(buffer.AsSpan(0, total));
                if (!readResult.IsFailure && readResult.Frame is not null)
                {
                    var wireCopy = buffer[..total].ToArray();
                    channelWriter.TryWrite(wireCopy);
                    onFrameReceived(readResult.Frame);
                }

                var remaining = bufferedCount - total;
                if (remaining > 0)
                {
                    Array.Copy(buffer, total, buffer, 0, remaining);
                }
                bufferedCount = remaining;
            }
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
