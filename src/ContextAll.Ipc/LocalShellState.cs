using ContextAll.Protocol;

namespace ContextAll.Ipc;

public enum LocalConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
}

public sealed record LocalTransferHistoryEntry(
    string TransferId,
    string DeviceId,
    string DeviceName,
    string Direction,
    string FileName,
    long TotalSize,
    string Outcome,
    DateTimeOffset EndedAt,
    string SavedDirectory);

public sealed record LocalShellState
{
    public const int MaxHistoryEntries = 100;

    public static LocalShellState Empty { get; } = new();

    public LocalConnectionStatus ConnectionStatus { get; init; } = LocalConnectionStatus.Disconnected;

    public bool Connected => ConnectionStatus == LocalConnectionStatus.Connected;

    public string LocalDeviceId { get; init; } = "";

    public string LocalDisplayName { get; init; } = "";

    public string DownloadDirectory { get; init; } = "";

    public string ResolvedDownloadDirectory { get; init; } = "";

    public IReadOnlyList<string> LocalAddresses { get; init; } = [];

    public int QuicPort { get; init; }

    public string ClipboardBackend { get; init; } = "unavailable";

    public IReadOnlyList<DeviceSnapshotPayload> Devices { get; init; } = [];

    public PairingSnapshotPayload? PairingPrompt { get; init; }

    public IReadOnlyList<FilePromptSnapshotPayload> FilePrompts { get; init; } = [];

    public IReadOnlyList<TransferSnapshotPayload> ActiveTransfers { get; init; } = [];

    public IReadOnlyList<LocalTransferHistoryEntry> History { get; init; } = [];

    public string? LastDisconnectReason { get; init; }

    public static LocalShellState Disconnected(string? reason = null) =>
        new()
        {
            ConnectionStatus = LocalConnectionStatus.Disconnected,
            LastDisconnectReason = reason,
        };
}

public static class LocalShellReducer
{
    public static LocalShellState OnConnecting(LocalShellState state) =>
        state with
        {
            ConnectionStatus = state.Connected ? LocalConnectionStatus.Connected : LocalConnectionStatus.Connecting,
        };

    public static LocalShellState OnDisconnected(LocalShellState state, string? reason = null) =>
        state with
        {
            ConnectionStatus = LocalConnectionStatus.Disconnected,
            Devices = [],
            PairingPrompt = null,
            FilePrompts = [],
            ActiveTransfers = [],
            LastDisconnectReason = reason,
        };

    public static LocalShellState Apply(LocalShellState state, Frame frame, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);

        return frame.Type switch
        {
            FrameTypes.Snapshot => ApplySnapshot(state, frame),
            FrameTypes.DeviceChanged => ApplyDeviceChanged(state, frame),
            FrameTypes.PairingPrompt => ApplyPairingPrompt(state, frame),
            FrameTypes.PairingEnded => ApplyPairingEnded(state, frame),
            FrameTypes.FilePrompt => ApplyFilePrompt(state, frame),
            FrameTypes.TransferProgress => ApplyTransferProgress(state, frame),
            FrameTypes.TransferEnded => ApplyTransferEnded(state, frame, clock),
            _ => state,
        };
    }

    static LocalShellState ApplySnapshot(LocalShellState state, Frame frame)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        var s = decoded.Value;
        return state with
        {
            ConnectionStatus = LocalConnectionStatus.Connected,
            LocalDeviceId = s.LocalDeviceId,
            LocalDisplayName = s.LocalDisplayName,
            DownloadDirectory = s.DownloadDirectory,
            ResolvedDownloadDirectory = string.IsNullOrEmpty(s.ResolvedDownloadDirectory) ? s.DownloadDirectory : s.ResolvedDownloadDirectory,
            LocalAddresses = s.LocalAddresses is null ? [] : [.. s.LocalAddresses],
            QuicPort = s.QuicPort,
            ClipboardBackend = string.IsNullOrEmpty(s.ClipboardBackend) ? "unavailable" : s.ClipboardBackend,
            Devices = [.. s.Devices],
            PairingPrompt = s.Pairing,
            FilePrompts = [.. s.FilePrompts],
            ActiveTransfers = [.. s.Transfers],
            LastDisconnectReason = null,
        };
    }

    static LocalShellState ApplyDeviceChanged(LocalShellState state, Frame frame)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.DeviceEventPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        var d = decoded.Value;
        var updated = new DeviceSnapshotPayload(
            d.DeviceId,
            d.DisplayName,
            d.Trusted,
            d.Presence,
            d.ClipboardSync,
            d.AskBeforeSave,
            d.RemoteClipboardSync,
            d.Endpoint);

        var next = new List<DeviceSnapshotPayload>(state.Devices.Count + 1);
        var replaced = false;
        foreach (var item in state.Devices)
        {
            if (item.DeviceId == updated.DeviceId)
            {
                next.Add(updated);
                replaced = true;
            }
            else
            {
                next.Add(item);
            }
        }

        if (!replaced)
        {
            next.Add(updated);
        }

        var clearPrompt = updated.Trusted && state.PairingPrompt?.DeviceId == updated.DeviceId;
        return state with
        {
            Devices = next,
            PairingPrompt = clearPrompt ? null : state.PairingPrompt,
        };
    }

    static LocalShellState ApplyPairingPrompt(LocalShellState state, Frame frame)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PairingSnapshotPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        return state with { PairingPrompt = decoded.Value };
    }

    static LocalShellState ApplyPairingEnded(LocalShellState state, Frame frame)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PairingEndedPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        if (state.PairingPrompt?.DeviceId != decoded.Value.DeviceId)
        {
            return state;
        }

        return state with { PairingPrompt = null };
    }

    static LocalShellState ApplyFilePrompt(LocalShellState state, Frame frame)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FilePromptSnapshotPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        var prompt = decoded.Value;
        var next = new List<FilePromptSnapshotPayload>(state.FilePrompts.Count + 1);
        var replaced = false;
        foreach (var existing in state.FilePrompts)
        {
            if (existing.TransferId == prompt.TransferId)
            {
                next.Add(prompt);
                replaced = true;
            }
            else
            {
                next.Add(existing);
            }
        }

        if (!replaced)
        {
            next.Add(prompt);
        }

        return state with { FilePrompts = next };
    }

    static LocalShellState ApplyTransferProgress(LocalShellState state, Frame frame)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.TransferSnapshotPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        var transfer = decoded.Value;
        var nextPrompts = state.FilePrompts.Any(p => p.TransferId == transfer.TransferId)
            ? state.FilePrompts.Where(p => p.TransferId != transfer.TransferId).ToArray()
            : state.FilePrompts;

        var nextTransfers = new List<TransferSnapshotPayload>(state.ActiveTransfers.Count + 1);
        var replaced = false;
        foreach (var existing in state.ActiveTransfers)
        {
            if (existing.TransferId == transfer.TransferId)
            {
                nextTransfers.Add(transfer);
                replaced = true;
            }
            else
            {
                nextTransfers.Add(existing);
            }
        }

        if (!replaced)
        {
            nextTransfers.Add(transfer);
        }

        return state with
        {
            FilePrompts = nextPrompts,
            ActiveTransfers = nextTransfers,
        };
    }

    static LocalShellState ApplyTransferEnded(LocalShellState state, Frame frame, Func<DateTimeOffset>? clock)
    {
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.TransferEndedPayload);
        if (decoded.IsFailure || decoded.Value is null)
        {
            return state;
        }

        var ended = decoded.Value;
        var matchedActive = state.ActiveTransfers.FirstOrDefault(t => t.TransferId == ended.TransferId);
        var matchedPrompt = state.FilePrompts.FirstOrDefault(p => p.TransferId == ended.TransferId);

        var nextPrompts = matchedPrompt is null
            ? state.FilePrompts
            : state.FilePrompts.Where(p => p.TransferId != ended.TransferId).ToArray();
        var nextActive = matchedActive is null
            ? state.ActiveTransfers
            : state.ActiveTransfers.Where(t => t.TransferId != ended.TransferId).ToArray();

        if (matchedActive is null && matchedPrompt is null)
        {
            return state with { FilePrompts = nextPrompts, ActiveTransfers = nextActive };
        }

        var deviceId = matchedActive?.DeviceId ?? matchedPrompt?.DeviceId ?? "";
        var deviceName = state.Devices.FirstOrDefault(d => d.DeviceId == deviceId)?.DisplayName ?? deviceId;
        var direction = matchedActive?.Direction ?? "in";
        var fileName = matchedActive?.FileName ?? matchedPrompt?.FileName ?? "";
        var totalSize = matchedActive?.TotalSize ?? matchedPrompt?.TotalSize ?? 0;
        var savedDir = string.IsNullOrEmpty(state.ResolvedDownloadDirectory)
            ? state.DownloadDirectory
            : state.ResolvedDownloadDirectory;

        var entry = new LocalTransferHistoryEntry(
            ended.TransferId,
            deviceId,
            deviceName,
            direction,
            fileName,
            totalSize,
            ended.Outcome,
            (clock ?? (() => DateTimeOffset.UtcNow))(),
            savedDir);

        var nextHistory = new List<LocalTransferHistoryEntry>(Math.Min(state.History.Count + 1, LocalShellState.MaxHistoryEntries))
        {
            entry,
        };
        foreach (var existing in state.History)
        {
            if (existing.TransferId != entry.TransferId && nextHistory.Count < LocalShellState.MaxHistoryEntries)
            {
                nextHistory.Add(existing);
            }
        }

        return state with
        {
            FilePrompts = nextPrompts,
            ActiveTransfers = nextActive,
            History = nextHistory,
        };
    }
}
