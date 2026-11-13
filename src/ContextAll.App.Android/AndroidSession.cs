using Android.Content;
using ContextAll.App.Android.Platform;
using ContextAll.App.Android.Transport;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session;

namespace ContextAll.App.Android;

public static class AndroidSession
{
    static readonly object Sync = new();
    static readonly HashSet<string> SeenRequests = [];
    static SessionHost? primaryEngine;
    static SessionHost? activeEngine;
    static AndroidHostPaths? primaryPaths;
    static AndroidDiscovery? primaryDiscovery;
    static AndroidTransferSink? primaryTransferSink;

    public static event Action<IReadOnlyList<Frame>>? Published;

    public static bool IsRunning
    {
        get
        {
            lock (Sync)
            {
                return activeEngine is not null;
            }
        }
    }

    public static SessionHost? CurrentEngine
    {
        get
        {
            lock (Sync)
            {
                return activeEngine;
            }
        }
    }

    public static AndroidHostPaths? CurrentPaths
    {
        get
        {
            lock (Sync)
            {
                return primaryPaths;
            }
        }
    }

    public static AndroidDiscovery? CurrentDiscovery
    {
        get
        {
            lock (Sync)
            {
                return primaryDiscovery;
            }
        }
    }

    public static AndroidTransferSink? CurrentTransferSink
    {
        get
        {
            lock (Sync)
            {
                return primaryTransferSink;
            }
        }
    }

    public static int Run()
    {
        AndroidMsQuicBackend.Register();
        return SharedEffects.PayloadCap();
    }

    public static SessionHost Start(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AndroidMsQuicBackend.Register();

        lock (Sync)
        {
            if (primaryEngine is not null)
            {
                return primaryEngine;
            }

            var appContext = context.ApplicationContext ?? context;
            var paths = new AndroidHostPaths(appContext);
            var identityStart = IdentityStore.Start(paths.IdentityDirectory);
            if (!identityStart.Ok || identityStart.Material is null)
            {
                throw new InvalidOperationException("Android identity initialization failed.");
            }

            var initialName = DisplayNames.Initial(global::Android.OS.Build.Model ?? "Android");
            var settingsStart = SettingsStore.Start(paths.SettingsFile, initialName);
            var displayName = settingsStart.Ok && settingsStart.Document is { DisplayName.Length: > 0 } doc
                ? doc.DisplayName
                : initialName;

            var digest = DeviceIds.Digest(identityStart.Material.Spki);
            var clipboard = new AndroidClipboardSource();
            var discovery = new AndroidDiscovery(
                appContext,
                identityStart.Material.DeviceId,
                digest,
                displayName,
                QuicLink.Port);
            var transferSink = new AndroidTransferSink(appContext);

            var engine = new SessionHost(paths, clipboard, discovery, transferSink, QuicLink.Port);
            engine.Published += ForwardPublished;
            engine.Start(CancellationToken.None);

            SeenRequests.Clear();
            primaryPaths = paths;
            primaryDiscovery = discovery;
            primaryTransferSink = transferSink;
            primaryEngine = engine;
            activeEngine = engine;
            return engine;
        }
    }

    public static void Stop()
    {
        SessionHost? toDispose;
        lock (Sync)
        {
            toDispose = primaryEngine;
            if (ReferenceEquals(activeEngine, primaryEngine))
            {
                activeEngine = null;
            }

            primaryEngine = null;
            primaryPaths = null;
            primaryDiscovery = null;
            primaryTransferSink = null;
            SeenRequests.Clear();
        }

        if (toDispose is not null)
        {
            toDispose.Published -= ForwardPublished;
            toDispose.Dispose();
        }
    }

    public static Frame? Snapshot()
    {
        SessionHost? engine;
        lock (Sync)
        {
            engine = activeEngine;
        }

        return engine?.Snapshot();
    }

    public static Frame SubmitFrame(Frame commandFrame)
    {
        if (!LocalFrameTypes.IsCommand(commandFrame.Type))
        {
            return ResultFrame("0", false, "invalid");
        }

        // Invoke the exact binary frame + JSON control codec used by the desktop daemon, in-process without sockets.
        var encodedBuffer = new byte[FrameLimits.HeaderBytes + commandFrame.Payload.Length];
        if (!FrameCodec.TryEncode(encodedBuffer, commandFrame, out var written))
        {
            return ResultFrame("0", false, "invalid");
        }

        var decodedFrame = FrameCodec.Decode(encodedBuffer.AsSpan(0, written));
        if (decodedFrame.IsFailure || decodedFrame.Value is not { } roundTripped)
        {
            return ResultFrame("0", false, "invalid");
        }

        var decodedPayload = ControlJson.Decode(roundTripped.Payload.Span, ProtocolJsonContext.Default.LocalCommandPayload);
        if (decodedPayload.IsFailure || decodedPayload.Value is null || !RequestIdOk(decodedPayload.Value.RequestId))
        {
            return ResultFrame(decodedPayload.Value?.RequestId ?? "0", false, "invalid");
        }

        var body = decodedPayload.Value;
        SessionHost? engine;
        lock (Sync)
        {
            engine = activeEngine;
            if (engine is null)
            {
                return ResultFrame(body.RequestId, false, "not-running");
            }

            if (!SeenRequests.Add(body.RequestId))
            {
                return ResultFrame(body.RequestId, false, "duplicate-request");
            }
        }

        string? error;
        lock (engine.Gate)
        {
            error = engine.Apply(body, roundTripped.Type);
        }

        engine.FlushClipboard();
        return ResultFrame(body.RequestId, error is null, error);
    }

    public static CommandResultPayload SubmitCommand(byte commandType, LocalCommandPayload payload)
    {
        var frame = FrameMessages.Json(
            commandType,
            0,
            0,
            FrameTypes.SessionStream,
            payload,
            ProtocolJsonContext.Default.LocalCommandPayload);
        var replyFrame = SubmitFrame(frame);
        var decoded = ControlJson.Decode(replyFrame.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload);
        return !decoded.IsFailure && decoded.Value is not null
            ? decoded.Value
            : new CommandResultPayload(payload.RequestId, false, "decode-failed");
    }

    internal static IDisposable AttachEngineForTest(SessionHost testEngine)
    {
        SessionHost? previous;
        lock (Sync)
        {
            previous = activeEngine;
            if (previous is not null && !ReferenceEquals(previous, primaryEngine))
            {
                previous.Published -= ForwardPublished;
            }

            SeenRequests.Clear();
            activeEngine = testEngine;
            testEngine.Published += ForwardPublished;
        }

        return new TestAttachmentScope(testEngine, previous);
    }

    static void ForwardPublished(IReadOnlyList<Frame> frames) => Published?.Invoke(frames);

    static bool RequestIdOk(string id) =>
        id.Length is >= 1 and <= 64 && id.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    static Frame ResultFrame(string requestId, bool ok, string? error) =>
        FrameMessages.Json(
            FrameTypes.CommandResult,
            0,
            0,
            FrameTypes.SessionStream,
            new CommandResultPayload(requestId, ok, error),
            ProtocolJsonContext.Default.CommandResultPayload);

    sealed class TestAttachmentScope(SessionHost testEngine, SessionHost? previous) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (Sync)
            {
                testEngine.Published -= ForwardPublished;
                SeenRequests.Clear();
                activeEngine = previous ?? primaryEngine;
            }
        }
    }
}
