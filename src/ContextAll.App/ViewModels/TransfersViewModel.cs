using System.Diagnostics;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.ViewModels;

/// <summary>View model for the Transfers (传输) section: queued multi-file upload, prompts, progress, and history.</summary>
public sealed class TransfersViewModel : ViewModelBase
{
    private readonly ILocalDaemonClient _client;
    private readonly PerPeerUploadQueue _queue;
    private readonly Action<string> _openFolder;
    private LocalShellState _state = LocalShellState.Empty;
    private string? _selectedTargetPeerId;
    private string? _noticeMessage;
    private bool _noticeIsError;

    public TransfersViewModel(
        ILocalDaemonClient client,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null,
        Action<string>? openFolder = null,
        Action<Action>? dispatchToUi = null)
        : base(dispatchToUi)
    {
        _client = client;
        _queue = new PerPeerUploadQueue(client, directoryExists, fileExists);
        _openFolder = openFolder ?? OpenFolderWithXdg;
        _state = client.CurrentState;

        _queue.QueueChanged += () => DispatchToUi(() => OnPropertyChanged(nameof(PendingQueueItems)));
        _queue.SkippedDirectoryReported += msg => DispatchToUi(() => SetNotice(msg, isError: false));
        _queue.DispatchErrorReported += msg => DispatchToUi(() => SetNotice(msg, isError: true));
        _client.StateChanged += next => DispatchToUi(() => ApplyState(next));

        SyncSelectedPeer();
    }

    public event Action<string, bool>? NoticeNotification;

    public Func<CancellationToken, Task<IReadOnlyList<string>>>? MultiFilePicker { get; set; }

    public IReadOnlyList<DeviceSnapshotPayload> TrustedPeers =>
        [.. _state.Devices.Where(d => d.Trusted).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public bool HasTrustedPeers => TrustedPeers.Count > 0;

    public string? SelectedTargetPeerId
    {
        get => _selectedTargetPeerId;
        set => SetField(ref _selectedTargetPeerId, value);
    }

    public IReadOnlyList<FilePromptSnapshotPayload> FilePrompts => _state.FilePrompts;
    public IReadOnlyList<TransferSnapshotPayload> ActiveTransfers => _state.ActiveTransfers;
    public IReadOnlyList<QueuedUploadItem> PendingQueueItems => _queue.SnapshotItems();
    public IReadOnlyList<LocalTransferHistoryEntry> History => _state.History;

    public bool HasFilePrompts => FilePrompts.Count > 0;
    public bool HasActiveTransfers => ActiveTransfers.Count > 0;
    public bool HasPendingQueue => PendingQueueItems.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);

    public string? NoticeMessage
    {
        get => _noticeMessage;
        private set => SetField(ref _noticeMessage, value);
    }

    public bool NoticeIsError
    {
        get => _noticeIsError;
        private set => SetField(ref _noticeIsError, value);
    }

    public void ApplyState(LocalShellState state)
    {
        _state = state;
        _queue.ApplyState(state);
        SyncSelectedPeer();

        OnPropertyChanged(nameof(TrustedPeers));
        OnPropertyChanged(nameof(HasTrustedPeers));
        OnPropertyChanged(nameof(FilePrompts));
        OnPropertyChanged(nameof(HasFilePrompts));
        OnPropertyChanged(nameof(ActiveTransfers));
        OnPropertyChanged(nameof(HasActiveTransfers));
        OnPropertyChanged(nameof(PendingQueueItems));
        OnPropertyChanged(nameof(HasPendingQueue));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(HasHistory));
    }

    public void SelectTargetPeer(string? deviceId) => SelectedTargetPeerId = deviceId;

    public void DismissNotice() => SetNotice(null, isError: false);

    public int EnqueueFilesForPeer(string? deviceId, IReadOnlyList<string> paths)
    {
        var target = deviceId ?? SelectedTargetPeerId;
        if (string.IsNullOrWhiteSpace(target) ||
            !_state.Devices.Any(d => d.Trusted && string.Equals(d.DeviceId, target, StringComparison.Ordinal)))
        {
            SetNotice("请先选择一台已配对设备作为发送目标。", isError: true);
            return 0;
        }

        return _queue.Enqueue(target, paths);
    }

    public async Task<int> PickAndSendFilesAsync(CancellationToken ct = default)
    {
        if (MultiFilePicker is null)
        {
            return 0;
        }

        var chosen = await MultiFilePicker(ct);
        if (chosen.Count == 0)
        {
            return 0;
        }

        return EnqueueFilesForPeer(SelectedTargetPeerId, chosen);
    }

    public async Task<bool> AcceptFileAsync(string transferId, CancellationToken ct = default)
    {
        var result = await _client.AcceptFileAsync(transferId, ct);
        if (!result.Ok)
        {
            SetNotice(string.IsNullOrWhiteSpace(result.Error) ? "接收文件失败。" : result.Error, isError: true);
        }

        return result.Ok;
    }

    public async Task<bool> RejectFileAsync(string transferId, CancellationToken ct = default)
    {
        var result = await _client.RejectFileAsync(transferId, ct);
        if (!result.Ok)
        {
            SetNotice(string.IsNullOrWhiteSpace(result.Error) ? "拒绝文件失败。" : result.Error, isError: true);
        }

        return result.Ok;
    }

    public async Task<bool> CancelTransferAsync(string transferId, CancellationToken ct = default)
    {
        var result = await _client.CancelTransferAsync(transferId, ct);
        if (!result.Ok)
        {
            SetNotice(string.IsNullOrWhiteSpace(result.Error) ? "取消传输失败。" : result.Error, isError: true);
        }

        return result.Ok;
    }

    public bool OpenContainingFolder(LocalTransferHistoryEntry entry)
    {
        var folder = string.IsNullOrWhiteSpace(entry.SavedDirectory)
            ? _state.ResolvedDownloadDirectory
            : entry.SavedDirectory;

        if (string.IsNullOrWhiteSpace(folder))
        {
            SetNotice("当前传输记录未提供可打开的保存目录。", isError: true);
            return false;
        }

        _openFolder(folder);
        return true;
    }

    public string ResolveDeviceName(string deviceId)
    {
        var found = _state.Devices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
        return found is null || string.IsNullOrWhiteSpace(found.DisplayName) ? deviceId : found.DisplayName;
    }

    private void SyncSelectedPeer()
    {
        var trusted = TrustedPeers;
        if (_selectedTargetPeerId is not null &&
            trusted.Any(d => string.Equals(d.DeviceId, _selectedTargetPeerId, StringComparison.Ordinal)))
        {
            return;
        }

        SelectedTargetPeerId = trusted.Count > 0 ? trusted[0].DeviceId : null;
    }

    private void SetNotice(string? message, bool isError)
    {
        NoticeMessage = message;
        NoticeIsError = isError;
        OnPropertyChanged(nameof(HasNotice));
        if (!string.IsNullOrWhiteSpace(message))
        {
            NoticeNotification?.Invoke(message, isError);
        }
    }

    private static void OpenFolderWithXdg(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { folderPath },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch
        {
            // Desktop URL opener unavailable in restricted/headless sessions.
        }
    }
}
