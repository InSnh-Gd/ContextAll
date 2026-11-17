using System.Net.Sockets;
using System.Text;
using ContextAll.Protocol;
using ContextAll.Session;
using Tmds.DBus.Protocol;

namespace ContextAll.Host;

public interface ILinuxNotificationSink : IDisposable
{
    event Action<uint, string>? ActionInvoked;
    event Action<uint, uint>? NotificationClosed;

    Task<uint> NotifyAsync(
        uint replacesId,
        string summary,
        string body,
        string[] actions,
        CancellationToken cancellationToken = default);

    Task CloseNotificationAsync(uint notificationId, CancellationToken cancellationToken = default);
}

public sealed class DbusNotificationSink : ILinuxNotificationSink
{
    const string NotificationsService = "org.freedesktop.Notifications";
    const string NotificationsPath = "/org/freedesktop/Notifications";
    const string NotificationsInterface = "org.freedesktop.Notifications";

    readonly string busAddress;
    readonly SemaphoreSlim gate = new(1, 1);
    DBusConnection? connection;
    IDisposable? actionSubscription;
    IDisposable? closedSubscription;
    bool unavailable;
    bool disposed;

    DbusNotificationSink(string busAddress)
    {
        this.busAddress = busAddress;
    }

    public event Action<uint, string>? ActionInvoked;
    public event Action<uint, uint>? NotificationClosed;

    public static DbusNotificationSink? TryCreate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var address = DBusAddress.Session;
        return string.IsNullOrWhiteSpace(address) ? null : new DbusNotificationSink(address);
    }

    public async Task<uint> NotifyAsync(
        uint replacesId,
        string summary,
        string body,
        string[] actions,
        CancellationToken cancellationToken = default)
    {
        var conn = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (conn is null)
        {
            return 0;
        }

        try
        {
            var message = BuildNotifyMessage(conn, replacesId, summary, body, actions);
            return await conn.CallMethodAsync(
                message,
                static (Message msg, object? _) => msg.GetBodyReader().ReadUInt32(),
                null).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDbusTransportFailure(ex))
        {
            return 0;
        }
    }

    public async Task CloseNotificationAsync(uint notificationId, CancellationToken cancellationToken = default)
    {
        if (notificationId == 0)
        {
            return;
        }

        var conn = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        if (conn is null)
        {
            return;
        }

        try
        {
            var message = BuildCloseMessage(conn, notificationId);
            await conn.CallMethodAsync(message).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDbusTransportFailure(ex))
        {
            // Notification daemon disappeared or rejected close; ignore cleanly.
        }
    }

    static MessageBuffer BuildNotifyMessage(
        DBusConnection conn,
        uint replacesId,
        string summary,
        string body,
        string[] actions)
    {
        using var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(
            NotificationsService,
            NotificationsPath,
            NotificationsInterface,
            "Notify",
            "susssasa{sv}i",
            MessageFlags.None);
        writer.WriteString("ContextAll");
        writer.WriteUInt32(replacesId);
        writer.WriteString("contextall");
        writer.WriteString(summary);
        writer.WriteString(body);
        writer.WriteArray(actions);
        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("desktop-entry");
        writer.WriteVariantString("contextall");
        writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }

    static MessageBuffer BuildCloseMessage(DBusConnection conn, uint notificationId)
    {
        using var writer = conn.GetMessageWriter();
        writer.WriteMethodCallHeader(
            NotificationsService,
            NotificationsPath,
            NotificationsInterface,
            "CloseNotification",
            "u",
            MessageFlags.None);
        writer.WriteUInt32(notificationId);
        return writer.CreateMessage();
    }

    async Task<DBusConnection?> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (disposed || unavailable)
            {
                return null;
            }

            if (connection is not null)
            {
                return connection;
            }

            var candidate = new DBusConnection(busAddress);
            await candidate.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

            var actionSub = await candidate.WatchSignalAsync<(uint Id, string ActionKey)>(
                sender: null,
                path: NotificationsPath,
                @interface: NotificationsInterface,
                signal: "ActionInvoked",
                reader: static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    return (reader.ReadUInt32(), reader.ReadString());
                },
                handler: OnActionSignal,
                flags: ObserverFlags.None,
                emitOnCapturedContext: false,
                state: this).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

            var closedSub = await candidate.WatchSignalAsync<(uint Id, uint Reason)>(
                sender: null,
                path: NotificationsPath,
                @interface: NotificationsInterface,
                signal: "NotificationClosed",
                reader: static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    return (reader.ReadUInt32(), reader.ReadUInt32());
                },
                handler: OnClosedSignal,
                flags: ObserverFlags.None,
                emitOnCapturedContext: false,
                state: this).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

            connection = candidate;
            actionSubscription = actionSub;
            closedSubscription = closedSub;
            return candidate;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDbusTransportFailure(ex))
        {
            unavailable = true;
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    static void OnActionSignal(Notification<(uint Id, string ActionKey)> notification)
    {
        if (notification.HasValue && notification.State is DbusNotificationSink self)
        {
            self.ActionInvoked?.Invoke(notification.Value.Id, notification.Value.ActionKey);
        }
    }

    static void OnClosedSignal(Notification<(uint Id, uint Reason)> notification)
    {
        if (notification.HasValue && notification.State is DbusNotificationSink self)
        {
            self.NotificationClosed?.Invoke(notification.Value.Id, notification.Value.Reason);
        }
    }

    static bool IsDbusTransportFailure(Exception ex) =>
        ex is DBusExceptionBase or DBusConnectFailedException or SocketException or IOException or ObjectDisposedException or InvalidOperationException;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        actionSubscription?.Dispose();
        closedSubscription?.Dispose();
        connection?.Dispose();
        gate.Dispose();
    }
}

public sealed class LinuxNotificationCoordinator : IDisposable
{
    readonly record struct NotificationTarget(bool IsPairing, string Identifier);

    readonly record struct ActiveNotification(uint Id, long Generation, NotificationTarget Target);

    readonly SessionHost engine;
    readonly ILinuxNotificationSink sink;
    readonly string? runtimeDirectory;
    readonly Func<CancellationToken, Task>? activateShellOverride;
    readonly object sync = new();
    readonly Dictionary<string, ActiveNotification> byKey = [];
    readonly Dictionary<uint, string> keyById = [];
    long nextGeneration;
    bool disposed;

    public LinuxNotificationCoordinator(
        SessionHost engine,
        ILinuxNotificationSink sink,
        string? runtimeDirectory = null,
        Func<CancellationToken, Task>? activateShellOverride = null)
    {
        this.engine = engine;
        this.sink = sink;
        this.runtimeDirectory = runtimeDirectory;
        this.activateShellOverride = activateShellOverride;

        this.engine.Published += OnPublished;
        this.sink.ActionInvoked += OnActionInvoked;
        this.sink.NotificationClosed += OnNotificationClosed;
    }

    public void NotifyLocalDecision(byte commandType, LocalCommandPayload body)
    {
        switch (commandType)
        {
            case FrameTypes.AcceptPairing or FrameTypes.RejectPairing when body.DeviceId is not null:
                CloseTracked(PairKey(body.DeviceId));
                break;
            case FrameTypes.AcceptFile or FrameTypes.RejectFile when body.TransferId is not null:
                CloseTracked(FileKey(body.TransferId));
                break;
        }
    }

    void OnPublished(IReadOnlyList<Frame> frames)
    {
        foreach (var frame in frames)
        {
            switch (frame.Type)
            {
                case FrameTypes.PairingPrompt:
                {
                    var pairing = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PairingSnapshotPayload);
                    if (!pairing.IsFailure && pairing.Value is not null)
                    {
                        var key = PairKey(pairing.Value.DeviceId);
                        var body = $"{pairing.Value.DisplayName} 请求配对，确认码：{pairing.Value.Code}";
                        PublishPrompt(
                            key,
                            new NotificationTarget(true, pairing.Value.DeviceId),
                            "配对请求",
                            body,
                            ["default", "打开", "accept", "接受", "reject", "拒绝"]);
                    }

                    break;
                }
                case FrameTypes.PairingEnded:
                {
                    var ended = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PairingEndedPayload);
                    if (!ended.IsFailure && ended.Value is not null)
                    {
                        CloseTracked(PairKey(ended.Value.DeviceId));
                    }

                    break;
                }
                case FrameTypes.DeviceChanged:
                {
                    var changed = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.DeviceEventPayload);
                    if (!changed.IsFailure && changed.Value is { Trusted: true })
                    {
                        CloseTracked(PairKey(changed.Value.DeviceId));
                    }

                    break;
                }
                case FrameTypes.FilePrompt:
                {
                    var file = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FilePromptSnapshotPayload);
                    if (!file.IsFailure && file.Value is not null)
                    {
                        var key = FileKey(file.Value.TransferId);
                        var body = $"{file.Value.FileName}（{FormatSize(file.Value.TotalSize)}）";
                        PublishPrompt(
                            key,
                            new NotificationTarget(false, file.Value.TransferId),
                            "收到文件请求",
                            body,
                            ["default", "打开", "accept", "接收", "reject", "拒绝"]);
                    }

                    break;
                }
                case FrameTypes.TransferProgress:
                {
                    var progress = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.TransferSnapshotPayload);
                    if (!progress.IsFailure && progress.Value is not null)
                    {
                        CloseTracked(FileKey(progress.Value.TransferId));
                    }

                    break;
                }
                case FrameTypes.TransferEnded:
                {
                    var ended = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.TransferEndedPayload);
                    if (!ended.IsFailure && ended.Value is not null)
                    {
                        CloseTracked(FileKey(ended.Value.TransferId));
                    }

                    break;
                }
            }
        }
    }

    void PublishPrompt(string key, NotificationTarget target, string summary, string body, string[] actions)
    {
        uint replacesId;
        long generation;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            replacesId = byKey.TryGetValue(key, out var existing) ? existing.Id : 0;
            generation = ++nextGeneration;
            byKey[key] = new ActiveNotification(replacesId, generation, target);
        }

        _ = DispatchNotifyAsync(key, target, replacesId, generation, summary, body, actions);
    }

    async Task DispatchNotifyAsync(
        string key,
        NotificationTarget target,
        uint replacesId,
        long generation,
        string summary,
        string body,
        string[] actions)
    {
        uint id;
        try
        {
            id = await sink.NotifyAsync(replacesId, summary, body, actions).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (id == 0)
        {
            return;
        }

        var shouldCloseImmediately = false;
        lock (sync)
        {
            if (disposed || !byKey.TryGetValue(key, out var current) || current.Generation != generation)
            {
                shouldCloseImmediately = true;
            }
            else
            {
                if (current.Id != 0 && current.Id != id)
                {
                    keyById.Remove(current.Id);
                }

                byKey[key] = new ActiveNotification(id, generation, target);
                keyById[id] = key;
            }
        }

        if (shouldCloseImmediately)
        {
            await SafeCloseAsync(id).ConfigureAwait(false);
        }
    }

    void CloseTracked(string key)
    {
        uint idToClose = 0;
        lock (sync)
        {
            if (byKey.Remove(key, out var existing))
            {
                idToClose = existing.Id;
                if (idToClose != 0)
                {
                    keyById.Remove(idToClose);
                }
            }
        }

        if (idToClose != 0)
        {
            _ = SafeCloseAsync(idToClose);
        }
    }

    void OnActionInvoked(uint id, string actionKey)
    {
        if (actionKey == "default")
        {
            _ = ActivateShellAsync();
            return;
        }

        NotificationTarget target;
        lock (sync)
        {
            if (disposed || !keyById.TryGetValue(id, out var key))
            {
                return;
            }

            if (!byKey.TryGetValue(key, out var tracked))
            {
                keyById.Remove(id);
                return;
            }

            if (actionKey is not ("accept" or "reject"))
            {
                return;
            }

            target = tracked.Target;
            byKey.Remove(key);
            keyById.Remove(id);
        }

        ApplyDecision(target, accept: actionKey == "accept");
        _ = SafeCloseAsync(id);
    }

    void OnNotificationClosed(uint id, uint reason)
    {
        lock (sync)
        {
            if (keyById.Remove(id, out var key) &&
                byKey.TryGetValue(key, out var existing) &&
                existing.Id == id)
            {
                // Dismissing or timing out the banner only detaches tracking; the prompt itself stays pending.
                byKey.Remove(key);
            }
        }
    }

    void ApplyDecision(NotificationTarget target, bool accept)
    {
        var requestId = Guid.NewGuid().ToString("n");
        var frameType = target.IsPairing
            ? (accept ? FrameTypes.AcceptPairing : FrameTypes.RejectPairing)
            : (accept ? FrameTypes.AcceptFile : FrameTypes.RejectFile);

        var payload = target.IsPairing
            ? new LocalCommandPayload(requestId, target.Identifier, null, null, null, null)
            : new LocalCommandPayload(requestId, null, null, null, null, target.Identifier);

        lock (engine.Gate)
        {
            _ = engine.Apply(payload, frameType);
        }

        engine.FlushClipboard();
    }

    async Task ActivateShellAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        if (activateShellOverride is not null)
        {
            try
            {
                await activateShellOverride(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shell raise.
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            return;
        }

        var socketPath = Path.Combine(runtimeDirectory, "contextall", "shell.sock");
        if (!File.Exists(socketPath))
        {
            return;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cts.Token).ConfigureAwait(false);
            var command = Encoding.UTF8.GetBytes("ACTIVATE\n");
            await socket.SendAsync(command, SocketFlags.None, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Shell not running or unreachable; leave prompt untouched.
        }
    }

    async Task SafeCloseAsync(uint id)
    {
        try
        {
            await sink.CloseNotificationAsync(id).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort banner closure.
        }
    }

    static string PairKey(string deviceId) => "pair:" + deviceId;

    static string FileKey(string transferId) => "file:" + transferId;

    static string FormatSize(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KiB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.##} MiB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GiB",
        };

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            byKey.Clear();
            keyById.Clear();
        }

        engine.Published -= OnPublished;
        sink.ActionInvoked -= OnActionInvoked;
        sink.NotificationClosed -= OnNotificationClosed;
    }
}
