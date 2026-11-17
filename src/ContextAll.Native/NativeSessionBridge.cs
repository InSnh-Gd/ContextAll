using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextAll.Native.Transport;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Native;

public sealed class NativeSessionBridge : IDisposable
{
    readonly object sync = new();
    readonly HashSet<string> seenRequests = [];
    readonly BlockingCollection<string> events = new(new ConcurrentQueue<string>());
    readonly ConcurrentDictionary<long, PendingCommit> pendingCommits = new();

    SessionHost? host;
    BridgeHostPaths? paths;
    BridgeClipboardSource? clipboard;
    BridgeDiscovery? discovery;
    BridgeTransferSink? transferSink;
    long nextCommitId;
    bool disposed;

    public static NativeSessionBridge Shared { get; } = new();

    public static int RunSmoke()
    {
        PlatformCrypto.UseManagedSha256 = true;
        AndroidMsQuicBackend.Register();
        return SharedEffects.PayloadCap();
    }

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return host is not null && !disposed;
            }
        }
    }

    public SessionHost? CurrentHost
    {
        get
        {
            lock (sync)
            {
                return host;
            }
        }
    }

    public string Start(
        string noBackupDirectory,
        string filesDirectory,
        string initialDisplayName,
        int quicPort = QuicLink.Port,
        bool useNativeMsQuicBackend = true,
        bool deferInboundCommitToHost = true,
        byte[]? localPfx = null,
        byte[]? localSpki = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noBackupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);

        if (useNativeMsQuicBackend || (localPfx is { Length: > 0 } && localSpki is { Length: > 0 }))
        {
            PlatformCrypto.UseManagedSha256 = true;
        }

        if (useNativeMsQuicBackend)
        {
            PlatformCrypto.DiagnosticSink = WriteAndroidLog;
            AndroidMsQuicBackend.Register();
        }

        SessionHost startedHost;
        BridgeHostPaths createdPaths;
        lock (sync)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeSessionBridge));
            }

            if (host is not null)
            {
                return BuildStartJson(host);
            }

            DrainEventsUnlocked();

            createdPaths = new BridgeHostPaths(noBackupDirectory, filesDirectory);
            IdentityMaterial identityMaterial;
            IdentityMaterial? preloadedIdentity = null;
            if (localPfx is { Length: > 0 } || localSpki is { Length: > 0 })
            {
                if (localPfx is not { Length: > 0 } || localSpki is not { Length: > 0 })
                {
                    throw new InvalidOperationException("identity-init-failed");
                }

                var certPath = Path.Combine(createdPaths.IdentityDirectory, "device.crt");
                var keyPath = Path.Combine(createdPaths.IdentityDirectory, "device.key");
                var portable = File.Exists(certPath) && File.Exists(keyPath)
                    ? new PortableCredential(localPfx, certPath, keyPath)
                    : new PortableCredential(localPfx);
                identityMaterial = new IdentityMaterial("", localSpki, DeviceIds.Text(localSpki), portable);
                preloadedIdentity = identityMaterial;
            }
            else
            {
                var idStart = IdentityStore.Start(createdPaths.IdentityDirectory);
                if (!idStart.Ok || idStart.Material is null)
                {
                    throw new InvalidOperationException("identity-init-failed");
                }

                identityMaterial = idStart.Material;
            }

            var safeName = DisplayNames.Initial(string.IsNullOrWhiteSpace(initialDisplayName) ? "Android" : initialDisplayName);
            _ = SettingsStore.Start(createdPaths.SettingsFile, safeName);

            var digest = DeviceIds.Digest(identityMaterial.Spki);
            var clip = new BridgeClipboardSource(OnHostClipboardText);
            var disc = new BridgeDiscovery(
                identityMaterial.DeviceId,
                digest,
                safeName,
                quicPort,
                OnDiscoveryAnnounce);
            var sink = new BridgeTransferSink(deferInboundCommitToHost ? CommitViaHost : null);

            var engine = new SessionHost(createdPaths, clip, disc, sink, quicPort, preloadedIdentity);
            engine.Published += OnHostPublished;
            engine.Start(CancellationToken.None);

            seenRequests.Clear();
            paths = createdPaths;
            clipboard = clip;
            discovery = disc;
            transferSink = sink;
            host = engine;
            startedHost = engine;
        }

        return BuildStartJson(startedHost);
    }

    public void Stop()
    {
        SessionHost? toDispose;
        lock (sync)
        {
            toDispose = host;
            host = null;
            paths = null;
            clipboard = null;
            discovery = null;
            transferSink = null;
            seenRequests.Clear();
        }

        CancelAllPendingCommits();
        if (toDispose is not null)
        {
            toDispose.Published -= OnHostPublished;
            toDispose.Dispose();
        }

        EnqueueEvent("""{"kind":"stopped"}""");
    }

    public string ResetIdentity(
        string noBackupDirectory,
        string filesDirectory,
        string initialDisplayName,
        int quicPort = QuicLink.Port,
        bool useNativeMsQuicBackend = true,
        bool deferInboundCommitToHost = true,
        byte[]? localPfx = null,
        byte[]? localSpki = null)
    {
        Stop();
        if (localPfx is not { Length: > 0 })
        {
            var identityDir = Path.Combine(noBackupDirectory, "identity");
            if (Directory.Exists(identityDir))
            {
                Directory.Delete(identityDir, recursive: true);
            }
        }

        return Start(
            noBackupDirectory,
            filesDirectory,
            initialDisplayName,
            quicPort,
            useNativeMsQuicBackend,
            deferInboundCommitToHost,
            localPfx,
            localSpki);
    }

    public string? GetSnapshotJson()
    {
        SessionHost? active;
        lock (sync)
        {
            active = host;
        }

        if (active is null)
        {
            return null;
        }

        var frame = active.Snapshot();
        return BuildSnapshotEnvelopeJson(active, frame);
    }

    public string SubmitCommandJson(byte frameType, string commandJson)
    {
        if (!LocalFrameTypes.IsCommand(frameType))
        {
            return EncodeCommandResult("0", false, "invalid");
        }

        var rawPayload = Encoding.UTF8.GetBytes(commandJson);
        var commandFrame = new Frame(rawPayload.Length, frameType, 0, 0, FrameTypes.SessionStream, rawPayload);
        var encodedBuffer = new byte[FrameLimits.HeaderBytes + commandFrame.Payload.Length];
        if (!FrameCodec.TryEncode(encodedBuffer, commandFrame, out var written))
        {
            return EncodeCommandResult("0", false, "invalid");
        }

        var decodedFrame = FrameCodec.Decode(encodedBuffer.AsSpan(0, written));
        if (decodedFrame.IsFailure || decodedFrame.Value is not { } roundTripped)
        {
            return EncodeCommandResult("0", false, "invalid");
        }

        var decodedPayload = ControlJson.Decode(roundTripped.Payload.Span, ProtocolJsonContext.Default.LocalCommandPayload);
        if (decodedPayload.IsFailure || decodedPayload.Value is null || !LocalOperator.RequestIdOk(decodedPayload.Value.RequestId))
        {
            return EncodeCommandResult(decodedPayload.Value?.RequestId ?? "0", false, "invalid");
        }

        var body = decodedPayload.Value;
        SessionHost? engine;
        lock (sync)
        {
            engine = host;
            if (engine is null)
            {
                return EncodeCommandResult(body.RequestId, false, "not-running");
            }

            var duplicate = LocalOperator.Remember(seenRequests, body.RequestId);
            if (duplicate is not null)
            {
                return EncodeCommandResult(body.RequestId, false, duplicate);
            }
        }

        string? error;
        lock (engine.Gate)
        {
            error = engine.Apply(body, roundTripped.Type);
        }

        engine.FlushClipboard();
        return EncodeCommandResult(body.RequestId, error is null, error);
    }

    public string ConnectEndpoint(string hostAddress, int port, string? alias)
    {
        SessionHost? engine;
        lock (sync)
        {
            engine = host;
        }

        if (engine is null)
        {
            return EncodeEndpointConnectResult(false, null, "not-running");
        }

        if (!IPAddress.TryParse(hostAddress.Trim(), out var ip) || port <= 0 || port > ushort.MaxValue)
        {
            return EncodeEndpointConnectResult(false, null, "invalid-endpoint");
        }

        var endpoint = new IPEndPoint(ip, port);
        (bool Ok, string? DeviceId, string? Error) result = (false, null, "connect-failed");
        using var done = new ManualResetEventSlim(false);
        _ = Task.Run(async () =>
        {
            try
            {
                result = await engine.ConnectEndpointAsync(endpoint, alias, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                result = (false, null, "connect-failed");
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait(TimeSpan.FromSeconds(8));
        return EncodeEndpointConnectResult(result.Ok, result.DeviceId, result.Error);
    }

    public string? PollEventJson(int timeoutMs)
    {
        if (disposed)
        {
            return null;
        }

        return events.TryTake(out var next, timeoutMs) ? next : null;
    }

    public void CompleteInboundCommit(long commitId, bool ok, string? committedTarget)
    {
        if (pendingCommits.TryRemove(commitId, out var pending))
        {
            pending.Complete(ok, committedTarget);
        }
    }

    public void PushClipboardText(string text)
    {
        BridgeClipboardSource? source;
        lock (sync)
        {
            source = clipboard;
        }

        source?.PushPlainText(text);
    }

    public void NotifyNetworkChanged(string prefixesCsv)
    {
        BridgeDiscovery? disc;
        lock (sync)
        {
            disc = discovery;
        }

        if (disc is null)
        {
            return;
        }

        var parsed = ParsePrefixes(prefixesCsv);
        disc.UpdatePrefixes(parsed);
    }

    public void PushDiscoveredPeer(string deviceId, string address, int port, string? displayName)
    {
        BridgeDiscovery? disc;
        lock (sync)
        {
            disc = discovery;
        }

        disc?.PushPeer(deviceId, address, port, displayName);
    }

    public void NotifyBeaconPacket(ReadOnlySpan<byte> packetBytes, string sourceAddress)
    {
        var decoded = BeaconCodec.Decode(packetBytes);
        if (decoded.IsFailure || decoded.Value is not { } beacon)
        {
            return;
        }

        var deviceId = "ca1" + Base32.Encode(beacon.Digest);
        BridgeDiscovery? disc;
        lock (sync)
        {
            disc = discovery;
        }

        disc?.PushPeer(deviceId, sourceAddress, beacon.Port, beacon.Name, beacon.Digest);
    }

    public byte[]? EncodeLocalBeacon(ushort port, string? overrideDisplayName = null)
    {
        SessionHost? engine;
        lock (sync)
        {
            engine = host;
        }

        if (engine is null)
        {
            return null;
        }

        var digest = DeviceIds.Digest(engine.LocalSpki);
        var name = string.IsNullOrWhiteSpace(overrideDisplayName) ? engine.DisplayName : overrideDisplayName;
        var encoded = BeaconCodec.Encode(digest, port, name);
        return !encoded.IsFailure ? encoded.Value : null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Stop();
        events.CompleteAdding();
        events.Dispose();
    }

    void OnHostPublished(IReadOnlyList<Frame> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "frames");
            writer.WriteStartArray("frames");
            foreach (var frame in frames)
            {
                writer.WriteStartObject();
                writer.WriteNumber("type", frame.Type);
                writer.WritePropertyName("payload");
                writer.WriteRawValue(frame.Payload.Span, skipInputValidation: true);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        EnqueueEvent(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    void OnHostClipboardText(string text)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "clipboard");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        EnqueueEvent(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    void OnDiscoveryAnnounce(string deviceId, byte[] digest, string displayName, ushort port)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "discoveryAnnounce");
            writer.WriteString("deviceId", deviceId);
            writer.WriteString("digestBase64", Convert.ToBase64String(digest));
            writer.WriteString("displayName", displayName);
            writer.WriteNumber("port", port);
            writer.WriteEndObject();
        }

        EnqueueEvent(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    bool CommitViaHost(
        string tempFilePath,
        string fileName,
        string rootHashHex,
        string downloadDirectory,
        [MaybeNullWhen(false)] out string committedTarget)
    {
        committedTarget = null;
        if (disposed || events.IsAddingCompleted)
        {
            return false;
        }

        var commitId = Interlocked.Increment(ref nextCommitId);
        using var pending = new PendingCommit();
        pendingCommits[commitId] = pending;

        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("kind", "commitInbound");
                writer.WriteNumber("commitId", commitId);
                writer.WriteString("tempFilePath", tempFilePath);
                writer.WriteString("fileName", fileName);
                writer.WriteString("rootHashHex", rootHashHex);
                writer.WriteString("downloadDirectory", downloadDirectory);
                writer.WriteEndObject();
            }

            EnqueueEvent(Encoding.UTF8.GetString(buffer.WrittenSpan));
            if (!pending.Wait(TimeSpan.FromSeconds(15)))
            {
                return false;
            }

            if (!pending.Ok)
            {
                return false;
            }

            committedTarget = pending.CommittedTarget ?? Path.Combine(downloadDirectory, DownloadNames.Sanitize(fileName));
            return true;
        }
        finally
        {
            pendingCommits.TryRemove(commitId, out _);
        }
    }

    void CancelAllPendingCommits()
    {
        foreach (var pair in pendingCommits)
        {
            if (pendingCommits.TryRemove(pair.Key, out var pending))
            {
                pending.Complete(false, null);
            }
        }
    }

    void EnqueueEvent(string json)
    {
        if (!disposed && !events.IsAddingCompleted)
        {
            try
            {
                events.Add(json);
            }
            catch (InvalidOperationException)
            {
                // Queue completed during shutdown.
            }
        }
    }

    void DrainEventsUnlocked()
    {
        while (events.TryTake(out _))
        {
        }
    }

    static string BuildStartJson(SessionHost startedHost)
    {
        var snapshotFrame = startedHost.Snapshot();
        var digest = DeviceIds.Digest(startedHost.LocalSpki);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteString("deviceId", DeviceIds.Text(startedHost.LocalSpki));
            writer.WriteString("digestBase64", Convert.ToBase64String(digest));
            writer.WriteString("displayName", startedHost.DisplayName);
            writer.WriteString("downloadsDirectory", startedHost.Downloads);
            writer.WriteNumber("port", QuicLink.Port);
            writer.WritePropertyName("snapshot");
            writer.WriteRawValue(snapshotFrame.Payload.Span, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static string BuildSnapshotEnvelopeJson(SessionHost active, Frame snapshotFrame)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", DeviceIds.Text(active.LocalSpki));
            writer.WriteString("displayName", active.DisplayName);
            writer.WriteString("downloadsDirectory", active.Downloads);
            writer.WritePropertyName("snapshot");
            writer.WriteRawValue(snapshotFrame.Payload.Span, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static string EncodeCommandResult(string requestId, bool ok, string? error)
    {
        var bytes = ControlJson.Encode(new CommandResultPayload(requestId, ok, error), ProtocolJsonContext.Default.CommandResultPayload);
        return bytes.Value is { Length: > 0 } utf8 ? Encoding.UTF8.GetString(utf8) : """{"requestId":"0","ok":false,"error":"encode"}""";
    }

    static string EncodeEndpointConnectResult(bool ok, string? deviceId, string? error)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", ok);
            if (deviceId is not null)
            {
                writer.WriteString("deviceId", deviceId);
            }
            if (error is not null)
            {
                writer.WriteString("error", error);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static unsafe void WriteAndroidLog(string message)
    {
        var writer = AndroidLogWriter.WritePtr;
        if (writer == 0)
        {
            return;
        }

        var tagBytes = "ContextAllNative\0"u8;
        var msgBytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* tagPtr = tagBytes)
        fixed (byte* msgPtr = msgBytes)
        {
            ((delegate* unmanaged[Cdecl]<int, byte*, byte*, int>)writer)(4, tagPtr, msgPtr);
        }
    }

    static class AndroidLogWriter
    {
        public static readonly nint WritePtr = Resolve();

        static nint Resolve()
        {
            if (NativeLibrary.TryLoad("liblog.so", out var handle)
                && NativeLibrary.TryGetExport(handle, "__android_log_write", out var symbol))
            {
                return symbol;
            }

            return 0;
        }
    }

    static List<OnLinkPrefix> ParsePrefixes(string csv)
    {
        var list = new List<OnLinkPrefix>();
        if (string.IsNullOrWhiteSpace(csv))
        {
            return list;
        }

        var parts = csv.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var slash = part.IndexOf('/');
            if (slash <= 0 || slash == part.Length - 1)
            {
                continue;
            }

            var ipText = part[..slash].Trim();
            var prefixText = part[(slash + 1)..].Trim();
            if (IPAddress.TryParse(ipText, out _)
                && int.TryParse(prefixText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var len)
                && len is >= 0 and <= 128)
            {
                list.Add(new OnLinkPrefix(ipText, len));
            }
        }

        return list;
    }

    sealed class PendingCommit : IDisposable
    {
        readonly ManualResetEventSlim gate = new(false);

        public bool Ok { get; private set; }
        public string? CommittedTarget { get; private set; }

        public void Complete(bool ok, string? committedTarget)
        {
            Ok = ok;
            CommittedTarget = committedTarget;
            gate.Set();
        }

        public bool Wait(TimeSpan timeout) => gate.Wait(timeout);

        public void Dispose() => gate.Dispose();
    }

    sealed class BridgeHostPaths : IHostPaths
    {
        public BridgeHostPaths(string noBackupDirectory, string filesDirectory)
        {
            IdentityDirectory = Path.Combine(noBackupDirectory, "identity");
            TransfersDirectory = Path.Combine(noBackupDirectory, "transfers");
            StateDirectory = noBackupDirectory;
            SettingsFile = Path.Combine(filesDirectory, "config.yaml");
            DefaultDownloadDirectory = Path.Combine(noBackupDirectory, "downloads-staging");
        }

        public string IdentityDirectory { get; }
        public string SettingsFile { get; }
        public string StateDirectory { get; }
        public string TransfersDirectory { get; }
        public string DefaultDownloadDirectory { get; }
    }

    sealed class BridgeClipboardSource(Action<string> onApplyText) : IClipboardSource
    {
        public bool IsSupported => true;
        public event Action<string, byte[]>? ClipboardChanged;
        public event Action<string>? PlainText;

        public void PushPlainText(string text)
        {
            PlainText?.Invoke(text);
            ClipboardChanged?.Invoke("text/plain;charset=utf-8", Encoding.UTF8.GetBytes(text));
        }

        public void SetText(string text) => onApplyText(text);

        public Task SetClipboardAsync(string mime, byte[] data, CancellationToken ct = default)
        {
            _ = mime;
            _ = data;
            _ = ct;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    sealed class BridgeDiscovery(
        string deviceId,
        byte[] digest,
        string initialDisplayName,
        int quicPort,
        Action<string, byte[], string, ushort> onAnnounce) : IDiscovery
    {
        readonly object gate = new();
        List<OnLinkPrefix> prefixes = [];
        string displayName = initialDisplayName;
        bool active;

        public bool IsActive
        {
            get
            {
                lock (gate)
                {
                    return active;
                }
            }
        }

        public bool OwnsNetworkAndSockets => true;
        public event Action? NetworkChanged;
        public event Action<DiscoveredPeer>? PeerDiscovered;

        public IReadOnlyList<OnLinkPrefix>? GetActivePrefixes()
        {
            lock (gate)
            {
                return prefixes.ToArray();
            }
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            _ = ct;
            lock (gate)
            {
                active = true;
            }

            onAnnounce(deviceId, digest, displayName, (ushort)quicPort);
            return Task.CompletedTask;
        }

        public Task AnnounceAsync(ushort port, CancellationToken ct = default)
        {
            _ = ct;
            string currentName;
            lock (gate)
            {
                currentName = displayName;
            }

            onAnnounce(deviceId, digest, currentName, port);
            return Task.CompletedTask;
        }

        public void UpdateDisplayName(string newDisplayName)
        {
            if (string.IsNullOrWhiteSpace(newDisplayName))
            {
                return;
            }

            lock (gate)
            {
                displayName = newDisplayName;
            }

            onAnnounce(deviceId, digest, newDisplayName, (ushort)quicPort);
        }

        public void Rebuild(IReadOnlyList<OnLinkPrefix> newPrefixes)
        {
            lock (gate)
            {
                prefixes = [.. newPrefixes];
            }
        }

        public void UpdatePrefixes(IReadOnlyList<OnLinkPrefix> next)
        {
            lock (gate)
            {
                prefixes = [.. next];
            }

            NetworkChanged?.Invoke();
        }

        public void PushPeer(string peerDeviceId, string address, int port, string? peerDisplayName, byte[]? peerDigest = null)
        {
            if (string.IsNullOrWhiteSpace(peerDeviceId) || string.IsNullOrWhiteSpace(address) || port <= 0 || port > ushort.MaxValue)
            {
                return;
            }

            PeerDiscovered?.Invoke(new DiscoveredPeer(peerDeviceId, address, port, peerDisplayName, peerDigest));
        }

        public void Dispose()
        {
            lock (gate)
            {
                active = false;
            }
        }
    }

    sealed class BridgeTransferSink(BridgeTransferSink.CommitCallback? hostCommit) : ITransferSink
    {
        public delegate bool CommitCallback(
            string tempFilePath,
            string fileName,
            string rootHashHex,
            string downloadDirectory,
            [MaybeNullWhen(false)] out string committedTarget);

        public bool HandlesCommitInbound => true;

        public bool TryCommitInbound(
            string tempFilePath,
            string fileName,
            string rootHashHex,
            string downloadDirectory,
            [MaybeNullWhen(false)] out string committedTarget)
        {
            committedTarget = null;
            if (!File.Exists(tempFilePath) || !VerifyChecksum(tempFilePath, rootHashHex))
            {
                return false;
            }

            if (hostCommit is not null)
            {
                return hostCommit(tempFilePath, fileName, rootHashHex, downloadDirectory, out committedTarget);
            }

            Directory.CreateDirectory(downloadDirectory);
            var safe = DownloadNames.Sanitize(fileName);
            var unique = DownloadNames.AvoidCollision(safe, candidate => File.Exists(Path.Combine(downloadDirectory, candidate)));
            var destination = Path.Combine(downloadDirectory, unique);
            File.Move(tempFilePath, destination);
            committedTarget = destination;
            return true;
        }

        public Task<string> CommitInboundFileAsync(string tempFilePath, string fileName, string rootHashHex, CancellationToken ct = default)
        {
            _ = ct;
            if (!TryCommitInbound(tempFilePath, fileName, rootHashHex, Path.GetDirectoryName(tempFilePath) ?? ".", out var committed))
            {
                throw new IOException("inbound-commit-failed");
            }

            return Task.FromResult(committed);
        }

        public bool VerifyChecksum(string filePath, string expectedSha256Hex)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            using var stream = File.OpenRead(filePath);
            var hash = PlatformCrypto.Sha256(stream);
            return string.Equals(Convert.ToHexStringLower(hash), expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
        }

        public void CleanupPartial(string tempFilePath)
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
