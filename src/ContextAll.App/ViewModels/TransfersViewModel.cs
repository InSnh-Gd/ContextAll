using System.Diagnostics;
using System.Text;
using System.Windows.Input;
using ContextAll.App.Controls;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.ViewModels;

public enum TransferListFilter
{
    All,
    Send,
    Receive,
    Completed,
}

/// <summary>View model for the Transfers (传输) section: queued multi-file upload, KPI summary, filters, text-to-file send, prompts, progress, and history.</summary>
public sealed class TransfersViewModel : ViewModelBase
{
    public const int SegmentSizeBytes = FrameLimits.MaxPayloadBytes - 36;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILocalDaemonClient _client;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<string> _openFolder;
    private readonly Dictionary<(string DeviceId, string FileName), string> _rememberedSendPathsByPeerAndName = new();
    private readonly Dictionary<string, string> _rememberedSendPathsByTransferId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clearedHistoryIds = new(StringComparer.Ordinal);

    private PerPeerUploadQueue _queue;
    private int _queueGeneration;
    private LocalShellState _state = LocalShellState.Empty;
    private string? _selectedTargetPeerId;
    private string? _noticeMessage;
    private bool _noticeIsError;
    private TransferListFilter _selectedFilter = TransferListFilter.All;
    private DateTimeOffset? _historyCutoff;

    private bool _isTextDialogOpen;
    private string _textFileNameDraft = string.Empty;
    private string _textContentDraft = string.Empty;
    private bool _textDialogFromClipboard;

    public TransfersViewModel(
        ILocalDaemonClient client,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null,
        Action<string>? openFolder = null,
        Action<Action>? dispatchToUi = null)
        : base(dispatchToUi)
    {
        _client = client;
        _directoryExists = directoryExists ?? Directory.Exists;
        _fileExists = fileExists ?? File.Exists;
        _openFolder = openFolder ?? OpenFolderWithXdg;
        _state = client.CurrentState;
        _queue = CreateQueue();

        _client.StateChanged += next => DispatchToUi(() => ApplyState(next));

        SelectPeerCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is string deviceId)
            {
                SelectTargetPeer(deviceId);
            }
        });
        SetFilterCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is TransferListFilter filter)
            {
                SetFilter(filter);
            }
        });
        AcceptFileCommand = new ShellRelayCommand(async parameter =>
        {
            if (parameter is string transferId)
            {
                await AcceptFileAsync(transferId);
            }
        });
        RejectFileCommand = new ShellRelayCommand(async parameter =>
        {
            if (parameter is string transferId)
            {
                await RejectFileAsync(transferId);
            }
        });
        CancelTransferCommand = new ShellRelayCommand(async parameter =>
        {
            if (parameter is string transferId)
            {
                await CancelTransferAsync(transferId);
            }
        });
        ResendHistoryCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is LocalTransferHistoryEntry entry)
            {
                ResendHistoryEntry(entry);
            }
        });
        OpenHistoryFolderCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is LocalTransferHistoryEntry entry)
            {
                OpenContainingFolder(entry);
            }
        });
        GoToDevicesCommand = new ShellRelayCommand(() => NavigateToDevicesRequested?.Invoke());

        SyncSelectedPeer();
    }

    public static string NotesTempDirectory => Path.Combine(Path.GetTempPath(), "contextall-notes");

    public event Action<string, bool>? NoticeNotification;

    public event Action? NavigateToDevicesRequested;

    public ICommand SelectPeerCommand { get; }

    public ICommand SetFilterCommand { get; }

    public ICommand AcceptFileCommand { get; }

    public ICommand RejectFileCommand { get; }

    public ICommand CancelTransferCommand { get; }

    public ICommand ResendHistoryCommand { get; }

    public ICommand OpenHistoryFolderCommand { get; }

    public ICommand GoToDevicesCommand { get; }

    public Func<CancellationToken, Task<IReadOnlyList<string>>>? MultiFilePicker { get; set; }

    public IReadOnlyList<DeviceSnapshotPayload> TrustedPeers =>
        [.. _state.Devices.Where(d => d.Trusted).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public bool HasTrustedPeers => TrustedPeers.Count > 0;

    public string? SelectedTargetPeerId
    {
        get => _selectedTargetPeerId;
        set
        {
            if (SetField(ref _selectedTargetPeerId, value))
            {
                OnPropertyChanged(nameof(PeerChipItems));
            }
        }
    }

    public TransferListFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetField(ref _selectedFilter, value))
            {
                RaiseFilterOutputsChanged();
            }
        }
    }

    public DateTimeOffset? HistoryClearedAt => _historyCutoff;

    public IReadOnlyList<FilePromptSnapshotPayload> FilePrompts => _state.FilePrompts;
    public IReadOnlyList<TransferSnapshotPayload> ActiveTransfers => _state.ActiveTransfers;
    public IReadOnlyList<QueuedUploadItem> PendingQueueItems => _queue.SnapshotItems();
    public IReadOnlyList<LocalTransferHistoryEntry> History => _state.History;

    public IReadOnlyList<LocalTransferHistoryEntry> VisibleHistory =>
        _historyCutoff is null
            ? _state.History
            : [.. _state.History.Where(h => h.EndedAt >= _historyCutoff.Value && !_clearedHistoryIds.Contains(h.TransferId))];

    public IReadOnlyList<FilePromptSnapshotPayload> FilteredPrompts =>
        SelectedFilter is TransferListFilter.All or TransferListFilter.Receive
            ? FilePrompts
            : [];

    public IReadOnlyList<TransferSnapshotPayload> FilteredActiveTransfers => SelectedFilter switch
    {
        TransferListFilter.All => ActiveTransfers,
        TransferListFilter.Send => [.. ActiveTransfers.Where(t => IsSendDirection(t.Direction))],
        TransferListFilter.Receive => [.. ActiveTransfers.Where(t => !IsSendDirection(t.Direction))],
        _ => [],
    };

    public IReadOnlyList<QueuedUploadItem> FilteredQueueItems =>
        SelectedFilter is TransferListFilter.All or TransferListFilter.Send
            ? PendingQueueItems
            : [];

    public IReadOnlyList<LocalTransferHistoryEntry> FilteredHistory => SelectedFilter switch
    {
        TransferListFilter.All or TransferListFilter.Completed => VisibleHistory,
        TransferListFilter.Send => [.. VisibleHistory.Where(h => IsSendDirection(h.Direction))],
        TransferListFilter.Receive => [.. VisibleHistory.Where(h => !IsSendDirection(h.Direction))],
        _ => [],
    };

    public int AllCount => ActiveTransfers.Count + FilePrompts.Count + PendingQueueItems.Count + VisibleHistory.Count;
    public int SendCount => ActiveTransfers.Count(t => IsSendDirection(t.Direction)) + PendingQueueItems.Count + VisibleHistory.Count(h => IsSendDirection(h.Direction));
    public int ReceiveCount => ActiveTransfers.Count(t => !IsSendDirection(t.Direction)) + FilePrompts.Count + VisibleHistory.Count(h => !IsSendDirection(h.Direction));
    public int CompletedCount => VisibleHistory.Count;

    public long TotalSentBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in _state.History)
            {
                if (IsSendDirection(entry.Direction) && IsCompletedOutcome(entry.Outcome))
                {
                    total += Math.Max(0, entry.TotalSize);
                }
            }

            foreach (var active in _state.ActiveTransfers)
            {
                if (IsSendDirection(active.Direction))
                {
                    total += ComputeActiveTransferredBytes(active);
                }
            }

            return total;
        }
    }

    public long TotalReceivedBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in _state.History)
            {
                if (!IsSendDirection(entry.Direction) && IsCompletedOutcome(entry.Outcome))
                {
                    total += Math.Max(0, entry.TotalSize);
                }
            }

            foreach (var active in _state.ActiveTransfers)
            {
                if (!IsSendDirection(active.Direction))
                {
                    total += ComputeActiveTransferredBytes(active);
                }
            }

            return total;
        }
    }

    public long CompletedSegmentsCount
    {
        get
        {
            long segments = 0;
            foreach (var entry in _state.History)
            {
                if (IsCompletedOutcome(entry.Outcome))
                {
                    segments += ComputeSegmentCount(entry.TotalSize);
                }
            }

            foreach (var active in _state.ActiveTransfers)
            {
                segments += Math.Max(0, active.CompletedSegments);
            }

            return segments;
        }
    }

    public int CompletedTransfersCount =>
        _state.History.Count(h => IsCompletedOutcome(h.Outcome));

    public string TotalSentLabel => ShellFormatting.Bytes(TotalSentBytes);

    public string TotalReceivedLabel => ShellFormatting.Bytes(TotalReceivedBytes);

    public string CompletedKpiLabel => $"{CompletedSegmentsCount} 段 · {CompletedTransfersCount} 次";

    public bool HasFilteredPrompts => FilteredPrompts.Count > 0;

    public bool HasFilteredActiveTransfers => FilteredActiveTransfers.Count > 0;

    public bool HasFilteredQueueItems => FilteredQueueItems.Count > 0;

    public bool HasFilteredHistory => FilteredHistory.Count > 0;

    public bool ShowPromptsSection => SelectedFilter is TransferListFilter.All or TransferListFilter.Receive;

    public bool ShowActiveSection => SelectedFilter is not TransferListFilter.Completed;

    public bool ShowQueueSection => SelectedFilter is TransferListFilter.All or TransferListFilter.Send;

    public string HistoryEmptyText => SelectedFilter == TransferListFilter.All
        ? "本次会话暂无传输记录"
        : "当前筛选条件下暂无传输记录";

    public IReadOnlyList<PeerChipItem> PeerChipItems =>
        [.. TrustedPeers.Select(peer => new PeerChipItem
        {
            DeviceId = peer.DeviceId,
            DisplayName = peer.DisplayName,
            IsSelected = string.Equals(peer.DeviceId, SelectedTargetPeerId, StringComparison.Ordinal),
            SelectCommand = SelectPeerCommand,
        })];

    public IReadOnlyList<FilterChipItem> FilterChipItems =>
    [
        new() { Filter = TransferListFilter.All, Label = $"全部 ({AllCount})", IsSelected = SelectedFilter == TransferListFilter.All, SelectCommand = SetFilterCommand },
        new() { Filter = TransferListFilter.Send, Label = $"发送 ({SendCount})", IsSelected = SelectedFilter == TransferListFilter.Send, SelectCommand = SetFilterCommand },
        new() { Filter = TransferListFilter.Receive, Label = $"接收 ({ReceiveCount})", IsSelected = SelectedFilter == TransferListFilter.Receive, SelectCommand = SetFilterCommand },
        new() { Filter = TransferListFilter.Completed, Label = $"已完成 ({CompletedCount})", IsSelected = SelectedFilter == TransferListFilter.Completed, SelectCommand = SetFilterCommand },
    ];

    public IReadOnlyList<PromptCardItem> PromptItems =>
        [.. FilteredPrompts.Select(prompt => new PromptCardItem
        {
            TransferId = prompt.TransferId,
            FileName = prompt.FileName,
            Subtitle = $"来自 {ResolveDeviceName(prompt.DeviceId)} · {ShellFormatting.Bytes(prompt.TotalSize)} · 剩余 {Math.Max(0, (int)Math.Ceiling(prompt.RemainingMilliseconds / 1000.0))} 秒",
            AcceptCommand = AcceptFileCommand,
            RejectCommand = RejectFileCommand,
        })];

    public IReadOnlyList<ActiveTransferItem> ActiveTransferItems
    {
        get
        {
            var transfers = FilteredActiveTransfers;
            var count = transfers.Count;
            var items = new ActiveTransferItem[count];
            for (var i = 0; i < count; i++)
            {
                var active = transfers[i];
                var pct = active.SegmentCount > 0
                    ? (int)Math.Clamp(active.CompletedSegments * 100.0 / active.SegmentCount, 0, 100)
                    : 0;
                var dirLabel = IsSendDirection(active.Direction) ? "发送" : "接收";
                items[i] = new ActiveTransferItem
                {
                    TransferId = active.TransferId,
                    Title = $"{dirLabel} · {active.FileName}",
                    Stats = $"{ResolveDeviceName(active.DeviceId)} · {ShellFormatting.Bytes(active.TotalSize)} · {pct}% · 分片 {active.CompletedSegments}/{active.SegmentCount}",
                    ProgressMaximum = Math.Max(1, active.SegmentCount),
                    ProgressValue = active.CompletedSegments,
                    MoveBlocked = active.MoveBlocked,
                    Position = GroupPositions.Of(i, count),
                    CancelCommand = CancelTransferCommand,
                };
            }

            return items;
        }
    }

    public IReadOnlyList<QueueRowItem> QueueRowItems
    {
        get
        {
            var items = FilteredQueueItems;
            var count = items.Count;
            var rows = new QueueRowItem[count];
            for (var i = 0; i < count; i++)
            {
                var item = items[i];
                var stateTag = item.IsInFlight ? "正在发送" : "排队等待";
                rows[i] = new QueueRowItem
                {
                    Label = $"[{stateTag}] {ResolveDeviceName(item.DeviceId)} → {item.FileName}",
                    Position = GroupPositions.Of(i, count),
                };
            }

            return rows;
        }
    }

    public IReadOnlyList<HistoryRowItem> HistoryRowItems
    {
        get
        {
            var entries = FilteredHistory;
            var count = entries.Count;
            var rows = new HistoryRowItem[count];
            for (var i = 0; i < count; i++)
            {
                var entry = entries[i];
                var dirTag = IsSendDirection(entry.Direction) ? "发送" : "接收";
                rows[i] = new HistoryRowItem
                {
                    Title = $"{entry.FileName} ({ShellFormatting.Outcome(entry.Outcome)})",
                    Meta = $"{dirTag} · {entry.DeviceName} · {ShellFormatting.Bytes(entry.TotalSize)} · {entry.EndedAt.LocalDateTime:HH:mm:ss}",
                    Position = GroupPositions.Of(i, count),
                    CanResend = CanResendHistoryEntry(entry),
                    CanOpenFolder = !IsSendDirection(entry.Direction) && !string.IsNullOrWhiteSpace(entry.SavedDirectory),
                    ResendCommand = new ShellRelayCommand(() => ResendHistoryEntry(entry)),
                    OpenFolderCommand = new ShellRelayCommand(() => OpenContainingFolder(entry)),
                };
            }

            return rows;
        }
    }

    public bool HasFilePrompts => FilePrompts.Count > 0;
    public bool HasActiveTransfers => ActiveTransfers.Count > 0;
    public bool HasPendingQueue => PendingQueueItems.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool HasVisibleHistory => VisibleHistory.Count > 0;
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);

    public bool IsTextDialogOpen
    {
        get => _isTextDialogOpen;
        private set => SetField(ref _isTextDialogOpen, value);
    }

    public string TextFileNameDraft
    {
        get => _textFileNameDraft;
        set => SetField(ref _textFileNameDraft, value);
    }

    public string TextContentDraft
    {
        get => _textContentDraft;
        set
        {
            if (SetField(ref _textContentDraft, value))
            {
                OnPropertyChanged(nameof(TextDraftUtf8Bytes));
                OnPropertyChanged(nameof(TextDraftSegmentCount));
            }
        }
    }

    public bool TextDialogFromClipboard
    {
        get => _textDialogFromClipboard;
        private set => SetField(ref _textDialogFromClipboard, value);
    }

    public int TextDraftUtf8Bytes => Encoding.UTF8.GetByteCount(_textContentDraft);
    public long TextDraftSegmentCount => ComputeSegmentCount(TextDraftUtf8Bytes);

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
        RecordRememberedTransferIds(state.History);
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
        OnPropertyChanged(nameof(VisibleHistory));
        OnPropertyChanged(nameof(HasVisibleHistory));
        RaiseKpisChanged();
        RaiseFilterOutputsChanged();
    }

    public void SelectTargetPeer(string? deviceId) => SelectedTargetPeerId = deviceId;

    public void SetFilter(TransferListFilter filter) => SelectedFilter = filter;

    public void DismissNotice() => SetNotice(null, isError: false);

    public void ClearHistory()
    {
        var maxEnded = _state.History.Count > 0
            ? _state.History.Max(h => h.EndedAt)
            : DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        _historyCutoff = maxEnded > now ? maxEnded : now;
        foreach (var entry in _state.History)
        {
            _clearedHistoryIds.Add(entry.TransferId);
        }

        OnPropertyChanged(nameof(HistoryClearedAt));
        OnPropertyChanged(nameof(VisibleHistory));
        OnPropertyChanged(nameof(HasVisibleHistory));
        RaiseFilterOutputsChanged();
        SetNotice("已清空本次会话的传输记录。", isError: false);
    }

    public void ClearQueue()
    {
        _queueGeneration++;
        _queue = CreateQueue();
        OnPropertyChanged(nameof(PendingQueueItems));
        OnPropertyChanged(nameof(HasPendingQueue));
        RaiseFilterOutputsChanged();
        SetNotice("已清空待发送队列。", isError: false);
    }

    public void OpenNewTextFileDialog()
    {
        TextDialogFromClipboard = false;
        TextFileNameDraft = string.Empty;
        TextContentDraft = string.Empty;
        IsTextDialogOpen = true;
    }

    public void OpenClipboardFileDialog(string? clipboardText)
    {
        TextDialogFromClipboard = true;
        TextFileNameDraft = $"clipboard-{DateTime.Now:HHmmss}.txt";
        TextContentDraft = clipboardText ?? string.Empty;
        IsTextDialogOpen = true;
    }

    public void CloseTextDialog() => IsTextDialogOpen = false;

    public bool SendTextDraftAsFile()
    {
        var created = CreateAndSendTextFile(TextFileNameDraft, TextContentDraft, SelectedTargetPeerId);
        if (created is null)
        {
            return false;
        }

        IsTextDialogOpen = false;
        TextFileNameDraft = string.Empty;
        TextContentDraft = string.Empty;
        return true;
    }

    public string? CreateAndSendTextFile(string? fileName, string content, string? deviceId = null)
    {
        var target = deviceId ?? SelectedTargetPeerId;
        if (string.IsNullOrWhiteSpace(target) ||
            !_state.Devices.Any(d => d.Trusted && string.Equals(d.DeviceId, target, StringComparison.Ordinal)))
        {
            SetNotice("请先选择一台已配对设备再发送文件。", isError: true);
            return null;
        }

        if (string.IsNullOrEmpty(content))
        {
            SetNotice("请输入或粘贴要发送的文本内容。", isError: true);
            return null;
        }

        var resolvedName = ResolveTextFileName(fileName);
        var notesDir = NotesTempDirectory;
        Directory.CreateDirectory(notesDir);

        var targetPath = Path.Combine(notesDir, resolvedName);
        if (PendingQueueItems.Any(q => string.Equals(q.FilePath, targetPath, StringComparison.Ordinal)))
        {
            var stem = Path.GetFileNameWithoutExtension(resolvedName);
            var ext = Path.GetExtension(resolvedName);
            targetPath = Path.Combine(notesDir, $"{stem}-{DateTime.Now:HHmmssfff}{ext}");
        }

        File.WriteAllText(targetPath, content, Utf8NoBom);
        var count = EnqueueFilesForPeer(target, [targetPath]);
        if (count > 0)
        {
            SetNotice($"已将文本保存为「{Path.GetFileName(targetPath)}」并加入发送队列。", isError: false);
            return targetPath;
        }

        return null;
    }

    public int EnqueueFilesForPeer(string? deviceId, IReadOnlyList<string> paths)
    {
        var target = deviceId ?? SelectedTargetPeerId;
        if (string.IsNullOrWhiteSpace(target) ||
            !_state.Devices.Any(d => d.Trusted && string.Equals(d.DeviceId, target, StringComparison.Ordinal)))
        {
            SetNotice("请先选择一台已配对设备作为发送目标。", isError: true);
            return 0;
        }

        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && !_directoryExists(path) && _fileExists(path))
            {
                var name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name))
                {
                    _rememberedSendPathsByPeerAndName[(target, name)] = path;
                }
            }
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

    public string? ResolveResendSourcePath(LocalTransferHistoryEntry entry)
    {
        if (!IsSendDirection(entry.Direction))
        {
            return null;
        }

        if (!_state.Devices.Any(d => d.Trusted && string.Equals(d.DeviceId, entry.DeviceId, StringComparison.Ordinal)))
        {
            return null;
        }

        if (_rememberedSendPathsByTransferId.TryGetValue(entry.TransferId, out var byId) && _fileExists(byId))
        {
            return byId;
        }

        if (_rememberedSendPathsByPeerAndName.TryGetValue((entry.DeviceId, entry.FileName), out var byName) && _fileExists(byName))
        {
            return byName;
        }

        if (!string.IsNullOrWhiteSpace(entry.SavedDirectory) && !string.IsNullOrWhiteSpace(entry.FileName))
        {
            var candidate = Path.Combine(entry.SavedDirectory, entry.FileName);
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public bool CanResendHistoryEntry(LocalTransferHistoryEntry entry) =>
        ResolveResendSourcePath(entry) is not null;

    public bool ResendHistoryEntry(LocalTransferHistoryEntry entry)
    {
        var path = ResolveResendSourcePath(entry);
        if (path is null)
        {
            SetNotice("无法重发：本地源文件不可用或目标设备已取消配对。", isError: true);
            return false;
        }

        var count = EnqueueFilesForPeer(entry.DeviceId, [path]);
        if (count > 0)
        {
            SetNotice($"已重新将「{entry.FileName}」加入发送队列。", isError: false);
            return true;
        }

        return false;
    }

    public bool OpenContainingFolder(LocalTransferHistoryEntry entry)
    {
        var folder = string.IsNullOrWhiteSpace(entry.SavedDirectory)
            ? _state.ResolvedDownloadDirectory
            : entry.SavedDirectory;

        if (string.IsNullOrWhiteSpace(folder))
        {
            SetNotice("当前记录没有可打开的保存位置。", isError: true);
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

    public static long ComputeSegmentCount(long byteCount) =>
        byteCount <= 0 ? 0 : (byteCount + SegmentSizeBytes - 1) / SegmentSizeBytes;

    public static bool IsSendDirection(string? direction) =>
        string.Equals(direction, "send", StringComparison.OrdinalIgnoreCase);

    public static bool IsCompletedOutcome(string? outcome) =>
        string.Equals(outcome, "done", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(outcome, "completed", StringComparison.OrdinalIgnoreCase);

    private PerPeerUploadQueue CreateQueue()
    {
        var generation = _queueGeneration;
        var proxyClient = new QueueClientProxy(_client, () => generation == _queueGeneration);
        var queue = new PerPeerUploadQueue(proxyClient, _directoryExists, _fileExists);
        queue.QueueChanged += () => DispatchToUi(() =>
        {
            if (generation != _queueGeneration)
            {
                return;
            }

            OnPropertyChanged(nameof(PendingQueueItems));
            OnPropertyChanged(nameof(HasPendingQueue));
            RaiseFilterOutputsChanged();
        });
        queue.SkippedDirectoryReported += msg => DispatchToUi(() =>
        {
            if (generation == _queueGeneration)
            {
                SetNotice(msg, isError: false);
            }
        });
        queue.DispatchErrorReported += msg => DispatchToUi(() =>
        {
            if (generation == _queueGeneration)
            {
                SetNotice(msg, isError: true);
            }
        });
        return queue;
    }

    private void RecordRememberedTransferIds(IReadOnlyList<LocalTransferHistoryEntry> history)
    {
        foreach (var entry in history)
        {
            if (!IsSendDirection(entry.Direction))
            {
                continue;
            }

            if (_rememberedSendPathsByTransferId.ContainsKey(entry.TransferId))
            {
                continue;
            }

            if (_rememberedSendPathsByPeerAndName.TryGetValue((entry.DeviceId, entry.FileName), out var knownPath))
            {
                _rememberedSendPathsByTransferId[entry.TransferId] = knownPath;
            }
        }
    }

    private static string ResolveTextFileName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return $"note-{DateTime.Now:HHmmss}.txt";
        }

        var leaf = Path.GetFileName(rawName.Trim());
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return $"note-{DateTime.Now:HHmmss}.txt";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(leaf.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or "..")
        {
            return $"note-{DateTime.Now:HHmmss}.txt";
        }

        return string.IsNullOrEmpty(Path.GetExtension(cleaned)) ? $"{cleaned}.txt" : cleaned;
    }

    private static long ComputeActiveTransferredBytes(TransferSnapshotPayload active)
    {
        if (active.TotalSize <= 0 || active.SegmentCount <= 0)
        {
            return 0;
        }

        var completed = Math.Clamp(active.CompletedSegments, 0, active.SegmentCount);
        return (long)((double)active.TotalSize * completed / active.SegmentCount);
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

    private void RaiseKpisChanged()
    {
        OnPropertyChanged(nameof(TotalSentBytes));
        OnPropertyChanged(nameof(TotalReceivedBytes));
        OnPropertyChanged(nameof(CompletedSegmentsCount));
        OnPropertyChanged(nameof(CompletedTransfersCount));
        OnPropertyChanged(nameof(TotalSentLabel));
        OnPropertyChanged(nameof(TotalReceivedLabel));
        OnPropertyChanged(nameof(CompletedKpiLabel));
    }

    private void RaiseFilterOutputsChanged()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(SendCount));
        OnPropertyChanged(nameof(ReceiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FilteredPrompts));
        OnPropertyChanged(nameof(FilteredActiveTransfers));
        OnPropertyChanged(nameof(FilteredQueueItems));
        OnPropertyChanged(nameof(FilteredHistory));
        OnPropertyChanged(nameof(HasFilteredPrompts));
        OnPropertyChanged(nameof(HasFilteredActiveTransfers));
        OnPropertyChanged(nameof(HasFilteredQueueItems));
        OnPropertyChanged(nameof(HasFilteredHistory));
        OnPropertyChanged(nameof(ShowPromptsSection));
        OnPropertyChanged(nameof(ShowActiveSection));
        OnPropertyChanged(nameof(ShowQueueSection));
        OnPropertyChanged(nameof(HistoryEmptyText));
        OnPropertyChanged(nameof(PeerChipItems));
        OnPropertyChanged(nameof(FilterChipItems));
        OnPropertyChanged(nameof(PromptItems));
        OnPropertyChanged(nameof(ActiveTransferItems));
        OnPropertyChanged(nameof(QueueRowItems));
        OnPropertyChanged(nameof(HistoryRowItems));
        OnPropertyChanged(nameof(TotalSentLabel));
        OnPropertyChanged(nameof(TotalReceivedLabel));
        OnPropertyChanged(nameof(CompletedKpiLabel));
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

    private sealed class QueueClientProxy(ILocalDaemonClient inner, Func<bool> isActive) : ILocalDaemonClient
    {
        public LocalShellState CurrentState => inner.CurrentState;

        public event Action<LocalShellState>? StateChanged
        {
            add => inner.StateChanged += value;
            remove => inner.StateChanged -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);

        public void RetryNow() => inner.RetryNow();

        public Task<CommandResultPayload> SendCommandAsync(
            byte frameType,
            LocalCommandPayload payload,
            CancellationToken cancellationToken = default)
        {
            if (!isActive())
            {
                return Task.FromResult(new CommandResultPayload(payload.RequestId, false, "queue-cleared"));
            }

            return inner.SendCommandAsync(frameType, payload, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
