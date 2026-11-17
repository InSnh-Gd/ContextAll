using ContextAll.Ipc;

namespace ContextAll.App.ViewModels;

/// <summary>Snapshot entry for a pending or currently dispatching outbound upload item.</summary>
public sealed record QueuedUploadItem(
    string DeviceId,
    string FilePath,
    string FileName,
    bool IsInFlight);

/// <summary>Coordinates sequential FIFO outbound file transfers per peer, skipping directories without archiving.</summary>
public sealed class PerPeerUploadQueue
{
    private readonly Lock _gate = new();
    private readonly ILocalDaemonClient _client;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, bool> _fileExists;
    private readonly Dictionary<string, PeerLane> _lanes = new(StringComparer.Ordinal);
    private LocalShellState _latestState = LocalShellState.Empty;

    public PerPeerUploadQueue(
        ILocalDaemonClient client,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        _client = client;
        _latestState = client.CurrentState;
        _directoryExists = directoryExists ?? Directory.Exists;
        _fileExists = fileExists ?? File.Exists;
    }

    public event Action? QueueChanged;
    public event Action<string>? SkippedDirectoryReported;
    public event Action<string>? DispatchErrorReported;

    public IReadOnlyList<QueuedUploadItem> SnapshotItems()
    {
        lock (_gate)
        {
            var items = new List<QueuedUploadItem>();
            foreach (var (deviceId, lane) in _lanes)
            {
                if (lane.InFlightPath is not null)
                {
                    items.Add(new QueuedUploadItem(deviceId, lane.InFlightPath, Path.GetFileName(lane.InFlightPath), IsInFlight: true));
                }

                foreach (var pending in lane.PendingPaths)
                {
                    items.Add(new QueuedUploadItem(deviceId, pending, Path.GetFileName(pending), IsInFlight: false));
                }
            }

            return items;
        }
    }

    public int Enqueue(string deviceId, IReadOnlyList<string> candidatePaths)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || candidatePaths.Count == 0)
        {
            return 0;
        }

        var skippedDirs = new List<string>();
        var filesToQueue = new List<string>();

        foreach (var raw in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (_directoryExists(raw))
            {
                var trimmed = raw.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dirName = Path.GetFileName(trimmed);
                skippedDirs.Add(string.IsNullOrEmpty(dirName) ? raw : dirName);
                continue;
            }

            if (_fileExists(raw))
            {
                filesToQueue.Add(raw);
            }
        }

        if (skippedDirs.Count > 0)
        {
            SkippedDirectoryReported?.Invoke(
                $"已跳过文件夹：{string.Join("、", skippedDirs)}（不支持直接发送或自动打包文件夹）");
        }

        if (filesToQueue.Count == 0)
        {
            return 0;
        }

        string? immediatePath = null;
        lock (_gate)
        {
            if (!_lanes.TryGetValue(deviceId, out var lane))
            {
                lane = new PeerLane();
                _lanes[deviceId] = lane;
            }

            foreach (var file in filesToQueue)
            {
                lane.PendingPaths.Enqueue(file);
            }

            immediatePath = TryTakeNextLocked(deviceId, lane);
        }

        QueueChanged?.Invoke();

        if (immediatePath is not null)
        {
            _ = DispatchSingleAsync(deviceId, immediatePath);
        }

        return filesToQueue.Count;
    }

    public void ApplyState(LocalShellState next)
    {
        List<(string DeviceId, string NextPath)>? dispatches = null;

        lock (_gate)
        {
            _latestState = next;
            foreach (var (deviceId, lane) in _lanes)
            {
                if (!lane.AwaitingTransferFinish || lane.DispatchInFlight)
                {
                    continue;
                }

                var hasActiveSend = next.ActiveTransfers.Any(t =>
                    string.Equals(t.DeviceId, deviceId, StringComparison.Ordinal) &&
                    string.Equals(t.Direction, "send", StringComparison.OrdinalIgnoreCase));

                if (hasActiveSend)
                {
                    lane.SawActiveSend = true;
                }

                if (HasCurrentTransferEnded(deviceId, lane, next, hasActiveSend))
                {
                    lane.AwaitingTransferFinish = false;
                    lane.InFlightPath = null;
                    lane.SawActiveSend = false;

                    var nextPath = TryTakeNextLocked(deviceId, lane);
                    if (nextPath is not null)
                    {
                        dispatches ??= [];
                        dispatches.Add((deviceId, nextPath));
                    }
                }
            }
        }

        QueueChanged?.Invoke();

        if (dispatches is not null)
        {
            foreach (var (deviceId, nextPath) in dispatches)
            {
                _ = DispatchSingleAsync(deviceId, nextPath);
            }
        }
    }

    private string? TryTakeNextLocked(string deviceId, PeerLane lane)
    {
        if (lane.DispatchInFlight || lane.AwaitingTransferFinish || lane.PendingPaths.Count == 0)
        {
            return null;
        }

        var path = lane.PendingPaths.Dequeue();
        lane.DispatchInFlight = true;
        lane.InFlightPath = path;
        lane.SawActiveSend = false;
        lane.BaselineSendHistoryCount = CountSendHistory(_latestState, deviceId);
        lane.BaselineLastSendEntry = LastSendHistory(_latestState, deviceId);
        return path;
    }

    private async Task DispatchSingleAsync(string deviceId, string filePath)
    {
        string? nextImmediate = null;
        string? errorToReport = null;

        try
        {
            var result = await _client.SendFileAsync(deviceId, filePath).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_lanes.TryGetValue(deviceId, out var lane))
                {
                    return;
                }

                lane.DispatchInFlight = false;
                if (!result.Ok)
                {
                    errorToReport = string.IsNullOrWhiteSpace(result.Error)
                        ? $"发送文件失败：{Path.GetFileName(filePath)}"
                        : result.Error;
                    lane.InFlightPath = null;
                    lane.AwaitingTransferFinish = false;
                    nextImmediate = TryTakeNextLocked(deviceId, lane);
                }
                else
                {
                    var hasActiveSend = _latestState.ActiveTransfers.Any(t =>
                        string.Equals(t.DeviceId, deviceId, StringComparison.Ordinal) &&
                        string.Equals(t.Direction, "send", StringComparison.OrdinalIgnoreCase));
                    if (hasActiveSend)
                    {
                        lane.SawActiveSend = true;
                    }

                    if (HasCurrentTransferEnded(deviceId, lane, _latestState, hasActiveSend))
                    {
                        lane.InFlightPath = null;
                        lane.AwaitingTransferFinish = false;
                        nextImmediate = TryTakeNextLocked(deviceId, lane);
                    }
                    else
                    {
                        lane.AwaitingTransferFinish = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_lanes.TryGetValue(deviceId, out var lane))
                {
                    lane.DispatchInFlight = false;
                    lane.AwaitingTransferFinish = false;
                    lane.InFlightPath = null;
                    nextImmediate = TryTakeNextLocked(deviceId, lane);
                }
            }

            errorToReport = ex.Message;
        }

        if (errorToReport is not null)
        {
            DispatchErrorReported?.Invoke(errorToReport);
        }

        QueueChanged?.Invoke();

        if (nextImmediate is not null)
        {
            await DispatchSingleAsync(deviceId, nextImmediate).ConfigureAwait(false);
        }
    }

    private static bool HasCurrentTransferEnded(
        string deviceId,
        PeerLane lane,
        LocalShellState state,
        bool hasActiveSend)
    {
        if (hasActiveSend)
        {
            return false;
        }

        if (lane.SawActiveSend)
        {
            return true;
        }

        var currentCount = CountSendHistory(state, deviceId);
        if (currentCount > lane.BaselineSendHistoryCount)
        {
            return true;
        }

        var latest = LastSendHistory(state, deviceId);
        return latest is not null && !Equals(latest, lane.BaselineLastSendEntry);
    }

    private static int CountSendHistory(LocalShellState state, string deviceId) =>
        state.History.Count(h =>
            string.Equals(h.DeviceId, deviceId, StringComparison.Ordinal) &&
            string.Equals(h.Direction, "send", StringComparison.OrdinalIgnoreCase));

    private static LocalTransferHistoryEntry? LastSendHistory(LocalShellState state, string deviceId) =>
        state.History.FirstOrDefault(h =>
            string.Equals(h.DeviceId, deviceId, StringComparison.Ordinal) &&
            string.Equals(h.Direction, "send", StringComparison.OrdinalIgnoreCase));

    private sealed class PeerLane
    {
        public Queue<string> PendingPaths { get; } = new();
        public bool DispatchInFlight { get; set; }
        public bool AwaitingTransferFinish { get; set; }
        public string? InFlightPath { get; set; }
        public bool SawActiveSend { get; set; }
        public int BaselineSendHistoryCount { get; set; }
        public LocalTransferHistoryEntry? BaselineLastSendEntry { get; set; }
    }
}
