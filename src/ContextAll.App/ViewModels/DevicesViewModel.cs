using System.Text;
using System.Windows.Input;
using ContextAll.App.Controls;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.ViewModels;

/// <summary>View model for the Devices (设备) section: master-detail list, pairing prompt, peer switches, unpair confirm, clipboard push.</summary>
public sealed class DevicesViewModel : ViewModelBase
{
    private readonly ILocalDaemonClient _client;
    private LocalShellState _state = LocalShellState.Empty;
    private double _width = ShellLayout.TwoColumnWidth;
    private string? _selectedDeviceId;
    private string? _confirmingUnpairDeviceId;
    private bool _isClipboardModalOpen;
    private string _clipboardDraftText = string.Empty;
    private string? _statusMessage;
    private bool _statusIsError;

    public DevicesViewModel(ILocalDaemonClient client, Action<Action>? dispatchToUi = null)
        : base(dispatchToUi)
    {
        _client = client;
        _state = client.CurrentState;
        _client.StateChanged += OnClientStateChanged;
        SelectDeviceCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is string deviceId)
            {
                SelectDevice(deviceId);
            }
        });
        ClearSelectionCommand = new ShellRelayCommand(ClearSelection);
        StartPairingCommand = new ShellRelayCommand(async parameter =>
        {
            if (parameter is string deviceId)
            {
                await StartPairingAsync(deviceId);
            }
        });
        CopyDeviceIdCommand = new ShellRelayCommand(() =>
        {
            if (SelectedDevice is { } device)
            {
                CopyRequested?.Invoke(device.DeviceId, "已复制设备 ID");
            }
        });
        CopyEndpointCommand = new ShellRelayCommand(() =>
        {
            if (SelectedDevice?.Endpoint is { Length: > 0 } endpoint)
            {
                CopyRequested?.Invoke(endpoint, "已复制设备地址");
            }
        });
        SendFilesCommand = new ShellRelayCommand(() =>
        {
            if (SelectedDeviceId is { } deviceId)
            {
                SendFilesRequested?.Invoke(deviceId);
            }
        });
        OpenClipboardCommand = new ShellRelayCommand(OpenClipboardModal);
        RequestUnpairCommand = new ShellRelayCommand(() =>
        {
            if (SelectedDeviceId is { } deviceId)
            {
                RequestUnpair(deviceId);
            }
        });
        ToggleClipboardSyncCommand = new ShellRelayCommand(async parameter =>
        {
            if (SelectedDeviceId is { } deviceId && parameter is bool enabled)
            {
                await SetClipboardSyncAsync(deviceId, enabled);
            }
        });
        ToggleAskBeforeSaveCommand = new ShellRelayCommand(async parameter =>
        {
            if (SelectedDeviceId is { } deviceId && parameter is bool enabled)
            {
                await SetAskBeforeSaveAsync(deviceId, enabled);
            }
        });
    }

    public event Action<string, string>? CopyRequested;

    public event Action<string>? SendFilesRequested;

    public ICommand SelectDeviceCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    public ICommand StartPairingCommand { get; }

    public ICommand CopyDeviceIdCommand { get; }

    public ICommand CopyEndpointCommand { get; }

    public ICommand SendFilesCommand { get; }

    public ICommand OpenClipboardCommand { get; }

    public ICommand RequestUnpairCommand { get; }

    public ICommand ToggleClipboardSyncCommand { get; }

    public ICommand ToggleAskBeforeSaveCommand { get; }

    public event Action<string, bool>? StatusNotification;

    public IReadOnlyList<DeviceSnapshotPayload> TrustedDevices =>
        [.. _state.Devices.Where(d => d.Trusted).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<DeviceSnapshotPayload> DiscoveredDevices =>
        [.. _state.Devices.Where(d => !d.Trusted).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<DeviceListItem> TrustedDeviceItems => MapDeviceItems(TrustedDevices, trusted: true);

    public IReadOnlyList<DeviceListItem> DiscoveredDeviceItems => MapDeviceItems(DiscoveredDevices, trusted: false);

    public bool HasTrustedDevices => TrustedDevices.Count > 0;
    public bool HasDiscoveredDevices => DiscoveredDevices.Count > 0;
    public bool HasAnyDevices => _state.Devices.Count > 0;

    public const long PairingPromptTimeoutMs = 60_000;

    public static bool IsConnectedPresence(string? presence) =>
        presence is "connected" or "online";

    public static bool IsReachablePresence(string? presence) =>
        presence is "reachable" or "connecting";

    public static string FormatPresence(string? presence) => presence switch
    {
        "connected" or "online" => "已连接",
        "reachable" => "局域网可达",
        "connecting" => "连接中",
        _ => "离线",
    };

    public bool IsServiceConnected => _state.Connected;
    public bool HasLanAddresses => _state.LocalAddresses.Count > 0;
    public bool IsLanReady => _state.Connected && _state.LocalAddresses.Count > 0;
    public int OnlinePeerCount => _state.Devices.Count(d => IsConnectedPresence(d.Presence));
    public int OnlineTrustedCount => TrustedDevices.Count(d => IsConnectedPresence(d.Presence));
    public int TrustedCount => TrustedDevices.Count;

    public string LanStatusTitle => !_state.Connected
        ? "后台连接服务未启动"
        : _state.LocalAddresses.Count > 0
            ? "局域网直连已就绪"
            : "正在等待局域网地址";

    public string LanStatusSubtitle => !_state.Connected
        ? "点此前往连接页查看状态或手动连接"
        : _state.QuicPort > 0
            ? $"已连接 {OnlinePeerCount} 台 · 已配对 {TrustedCount} 台 · UDP {_state.QuicPort}"
            : $"已连接 {OnlinePeerCount} 台 · 已配对 {TrustedCount} 台 · 点此查看连接详情";

    public string LanStatusPill => !_state.Connected
        ? "未连接"
        : _state.LocalAddresses.Count > 0
            ? "运行中"
            : "待命";

    public int MutualClipboardPeerCount => TrustedDevices.Count(d =>
        IsConnectedPresence(d.Presence) && d.ClipboardSync == true && d.RemoteClipboardSync == true);

    public int LocalClipboardEnabledCount => TrustedDevices.Count(d => d.ClipboardSync == true);

    public string ClipboardQuickSummary
    {
        get
        {
            if (MutualClipboardPeerCount > 0)
            {
                return $"已与 {MutualClipboardPeerCount} 台在线设备开启双向同步";
            }

            return LocalClipboardEnabledCount > 0
                ? $"本机已为 {LocalClipboardEnabledCount} 台设备开启同步，等待对方在线并开启"
                : "在已配对设备详情中开启「剪贴板同步」即可共享文本";
        }
    }

    public int PairingRemainingSeconds => PairingPrompt is { } prompt
        ? (int)Math.Clamp((prompt.RemainingMilliseconds + 999L) / 1000L, 0, 60)
        : 0;

    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        private set
        {
            if (SetField(ref _selectedDeviceId, value))
            {
                ConfirmingUnpairDeviceId = null;
                NotifyLayoutProperties();
            }
        }
    }

    public DeviceSnapshotPayload? SelectedDevice =>
        SelectedDeviceId is null
            ? null
            : _state.Devices.FirstOrDefault(d => string.Equals(d.DeviceId, SelectedDeviceId, StringComparison.Ordinal));

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool SelectedDeviceTrusted => SelectedDevice?.Trusted == true;

    public string SelectedDisplayName => SelectedDevice?.DisplayName ?? string.Empty;

    public string SelectedDeviceIdText => SelectedDevice?.DeviceId ?? string.Empty;

    public string SelectedEndpointText => SelectedDevice?.Endpoint ?? string.Empty;

    public IReadOnlyList<LocalTransferHistoryEntry> SelectedDeviceHistory =>
        SelectedDeviceId is null
            ? []
            : [.. _state.History
                .Where(h => string.Equals(h.DeviceId, SelectedDeviceId, StringComparison.Ordinal))
                .OrderByDescending(h => h.EndedAt)];

    public IReadOnlyList<DeviceHistoryItem> SelectedDeviceHistoryItems
    {
        get
        {
            var history = SelectedDeviceHistory;
            var count = Math.Min(history.Count, 8);
            var items = new List<DeviceHistoryItem>(count);
            for (var i = 0; i < count; i++)
            {
                var entry = history[i];
                var direction = entry.Direction == "send" ? "已发出" : "已收到";
                items.Add(new DeviceHistoryItem
                {
                    Title = $"{direction} · {entry.FileName}",
                    Subtitle = $"{ShellFormatting.Outcome(entry.Outcome)} · {ShellFormatting.Bytes(entry.TotalSize)} · {entry.EndedAt.ToLocalTime():MM-dd HH:mm}",
                    Position = GroupPositions.Of(i, count),
                });
            }

            return items;
        }
    }

    public string SelectedPresenceLabel => FormatPresence(SelectedDevice?.Presence);

    public BadgeTone SelectedPresenceTone => ResolvePresenceTone(SelectedDevice?.Presence);

    public string SelectedRemoteClipboardLabel => SelectedDevice?.RemoteClipboardSync switch
    {
        true => "对端已开启剪贴板",
        false => "对端未开启剪贴板",
        _ => "对端状态未知",
    };

    public bool SelectedDeviceHasEndpoint => !string.IsNullOrWhiteSpace(SelectedDevice?.Endpoint);

    public bool SelectedClipboardSync => SelectedDevice?.ClipboardSync == true;

    public bool SelectedAskBeforeSave => SelectedDevice?.AskBeforeSave != false;

    public bool HasSelectedDeviceHistory => SelectedDeviceHistory.Count > 0;

    public bool SelectedDeviceClipboardDisabled =>
        SelectedDevice is { Trusted: true, ClipboardSync: false };

    public string ClipboardBroadcastClarification => SelectedDevice is { Trusted: true, ClipboardSync: false } selected
        ? $"推送将广播给所有已开启剪贴板同步的可信设备（注意：当前选中设备「{selected.DisplayName}」已关闭剪贴板同步）。"
        : "推送将广播给所有已开启剪贴板同步的可信设备。";

    public PairingSnapshotPayload? PairingPrompt => _state.PairingPrompt;

    public bool ShowListPane => ShellLayout.Columns(_width, SelectedDevice is not null).List;
    public bool ShowDetailPane => ShellLayout.Columns(_width, SelectedDevice is not null).Detail;
    public bool CanNavigateBack => !ShellLayout.TwoColumns(_width) && SelectedDevice is not null;

    public string? ConfirmingUnpairDeviceId
    {
        get => _confirmingUnpairDeviceId;
        private set => SetField(ref _confirmingUnpairDeviceId, value);
    }

    public bool IsClipboardModalOpen
    {
        get => _isClipboardModalOpen;
        private set => SetField(ref _isClipboardModalOpen, value);
    }

    public string ClipboardDraftText
    {
        get => _clipboardDraftText;
        set
        {
            if (SetField(ref _clipboardDraftText, value))
            {
                OnPropertyChanged(nameof(ClipboardUtf8Bytes));
                OnPropertyChanged(nameof(IsClipboardTooLarge));
            }
        }
    }

    public int ClipboardUtf8Bytes => Encoding.UTF8.GetByteCount(_clipboardDraftText);
    public bool IsClipboardTooLarge => ClipboardUtf8Bytes > FrameLimits.MaxClipboardBytes;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetField(ref _statusIsError, value);
    }

    public void ApplyState(LocalShellState state)
    {
        _state = state;
        if (_selectedDeviceId is not null &&
            !_state.Devices.Any(d => string.Equals(d.DeviceId, _selectedDeviceId, StringComparison.Ordinal)))
        {
            _selectedDeviceId = null;
            _confirmingUnpairDeviceId = null;
        }

        OnPropertyChanged(nameof(TrustedDevices));
        OnPropertyChanged(nameof(DiscoveredDevices));
        OnPropertyChanged(nameof(TrustedDeviceItems));
        OnPropertyChanged(nameof(DiscoveredDeviceItems));
        OnPropertyChanged(nameof(HasTrustedDevices));
        OnPropertyChanged(nameof(HasDiscoveredDevices));
        OnPropertyChanged(nameof(HasAnyDevices));
        OnPropertyChanged(nameof(SelectedDeviceId));
        OnPropertyChanged(nameof(ConfirmingUnpairDeviceId));
        OnPropertyChanged(nameof(PairingPrompt));
        OnPropertyChanged(nameof(PairingRemainingSeconds));
        OnPropertyChanged(nameof(IsServiceConnected));
        OnPropertyChanged(nameof(HasLanAddresses));
        OnPropertyChanged(nameof(IsLanReady));
        OnPropertyChanged(nameof(OnlinePeerCount));
        OnPropertyChanged(nameof(OnlineTrustedCount));
        OnPropertyChanged(nameof(TrustedCount));
        OnPropertyChanged(nameof(LanStatusTitle));
        OnPropertyChanged(nameof(LanStatusSubtitle));
        OnPropertyChanged(nameof(LanStatusPill));
        OnPropertyChanged(nameof(MutualClipboardPeerCount));
        OnPropertyChanged(nameof(LocalClipboardEnabledCount));
        OnPropertyChanged(nameof(ClipboardQuickSummary));
        NotifyLayoutProperties();
    }

    public void ApplyWidth(double width)
    {
        _width = width;
        NotifyLayoutProperties();
    }

    public void SelectDevice(string? deviceId) => SelectedDeviceId = deviceId;
    public void ClearSelection() => SelectedDeviceId = null;

    public void RequestUnpair(string deviceId) => ConfirmingUnpairDeviceId = deviceId;
    public void CancelUnpair() => ConfirmingUnpairDeviceId = null;

    public void OpenClipboardModal()
    {
        IsClipboardModalOpen = true;
        SetStatus(null, isError: false);
    }

    public void CloseClipboardModal() => IsClipboardModalOpen = false;

    public void NotifyCopied(string message) => SetStatus(message, isError: false);

    public async Task<bool> StartPairingAsync(string deviceId, CancellationToken ct = default)
    {
        var result = await _client.StartPairingAsync(deviceId, ct);
        ApplyResult(result, "已发起配对请求。");
        return result.Ok;
    }

    public async Task<bool> AcceptPairingAsync(CancellationToken ct = default)
    {
        var deviceId = PairingPrompt?.DeviceId;
        if (string.IsNullOrEmpty(deviceId))
        {
            return false;
        }

        var result = await _client.AcceptPairingAsync(deviceId, ct);
        ApplyResult(result, "已同意配对。");
        return result.Ok;
    }

    public async Task<bool> RejectPairingAsync(CancellationToken ct = default)
    {
        var deviceId = PairingPrompt?.DeviceId;
        if (string.IsNullOrEmpty(deviceId))
        {
            return false;
        }

        var result = await _client.RejectPairingAsync(deviceId, ct);
        ApplyResult(result, "已拒绝配对。");
        return result.Ok;
    }

    public async Task<bool> SetClipboardSyncAsync(string deviceId, bool enabled, CancellationToken ct = default)
    {
        var result = await _client.SetClipboardSyncAsync(deviceId, enabled, ct);
        ApplyResult(result, enabled ? "已开启剪贴板同步。" : "已关闭剪贴板同步。");
        return result.Ok;
    }

    public async Task<bool> SetAskBeforeSaveAsync(string deviceId, bool enabled, CancellationToken ct = default)
    {
        var result = await _client.SetAskBeforeSaveAsync(deviceId, enabled, ct);
        ApplyResult(result, enabled ? "接收文件前将先询问。" : "接收文件将自动保存。");
        return result.Ok;
    }

    public async Task<bool> ConfirmUnpairAsync(CancellationToken ct = default)
    {
        var deviceId = ConfirmingUnpairDeviceId;
        if (string.IsNullOrEmpty(deviceId))
        {
            return false;
        }

        var result = await _client.UnpairAsync(deviceId, ct);
        ConfirmingUnpairDeviceId = null;
        if (result.Ok && string.Equals(SelectedDeviceId, deviceId, StringComparison.Ordinal))
        {
            SelectedDeviceId = null;
        }

        ApplyResult(result, "已取消配对。");
        return result.Ok;
    }

    public async Task<bool> PushClipboardAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_clipboardDraftText))
        {
            SetStatus("请输入要发送的纯文本内容。", isError: true);
            return false;
        }

        if (IsClipboardTooLarge)
        {
            SetStatus("文本超过 1 MiB 上限，请改用文件发送。", isError: true);
            return false;
        }

        var result = await _client.PushClipboardTextAsync(_clipboardDraftText, ct);
        if (result.Ok)
        {
            IsClipboardModalOpen = false;
            ClipboardDraftText = string.Empty;
        }

        ApplyResult(result, "已发送剪贴板文本。");
        return result.Ok;
    }

    private void OnClientStateChanged(LocalShellState next) => DispatchToUi(() => ApplyState(next));

    private void NotifyLayoutProperties()
    {
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(SelectedDeviceTrusted));
        OnPropertyChanged(nameof(SelectedDisplayName));
        OnPropertyChanged(nameof(SelectedDeviceIdText));
        OnPropertyChanged(nameof(SelectedEndpointText));
        OnPropertyChanged(nameof(SelectedDeviceHistory));
        OnPropertyChanged(nameof(SelectedDeviceHistoryItems));
        OnPropertyChanged(nameof(HasSelectedDeviceHistory));
        OnPropertyChanged(nameof(SelectedDeviceClipboardDisabled));
        OnPropertyChanged(nameof(ClipboardBroadcastClarification));
        OnPropertyChanged(nameof(SelectedPresenceLabel));
        OnPropertyChanged(nameof(SelectedPresenceTone));
        OnPropertyChanged(nameof(SelectedRemoteClipboardLabel));
        OnPropertyChanged(nameof(SelectedDeviceHasEndpoint));
        OnPropertyChanged(nameof(SelectedClipboardSync));
        OnPropertyChanged(nameof(SelectedAskBeforeSave));
        OnPropertyChanged(nameof(TrustedDeviceItems));
        OnPropertyChanged(nameof(DiscoveredDeviceItems));
        OnPropertyChanged(nameof(ShowListPane));
        OnPropertyChanged(nameof(ShowDetailPane));
        OnPropertyChanged(nameof(CanNavigateBack));
    }

    DeviceListItem[] MapDeviceItems(IReadOnlyList<DeviceSnapshotPayload> devices, bool trusted)
    {
        var count = devices.Count;
        var items = new DeviceListItem[count];
        for (var i = 0; i < count; i++)
        {
            var device = devices[i];
            items[i] = new DeviceListItem
            {
                Device = device,
                Position = GroupPositions.Of(i, count),
                IsSelected = string.Equals(device.DeviceId, SelectedDeviceId, StringComparison.Ordinal),
                IsTrusted = trusted,
                PresenceLabel = FormatPresence(device.Presence),
                PresenceTone = ResolvePresenceTone(device.Presence),
                ClipboardPolicy = device.ClipboardSync == true ? "剪贴板同步开" : "剪贴板同步关",
                ClipboardPolicyOn = device.ClipboardSync == true,
                SavePolicy = device.AskBeforeSave != false ? "接收前询问" : "自动保存",
                Subtitle = string.IsNullOrWhiteSpace(device.Endpoint)
                    ? $"{ShellFormatting.ShortId(device.DeviceId)} · 点击发起配对"
                    : $"{ShellFormatting.ShortId(device.DeviceId)} · {device.Endpoint}",
                SelectCommand = SelectDeviceCommand,
                StartPairingCommand = trusted ? null : StartPairingCommand,
            };
        }

        return items;
    }

    static BadgeTone ResolvePresenceTone(string? presence)
    {
        if (IsConnectedPresence(presence))
        {
            return BadgeTone.Primary;
        }

        return IsReachablePresence(presence) ? BadgeTone.Secondary : BadgeTone.Neutral;
    }

    private void ApplyResult(CommandResultPayload result, string successMessage)
    {
        if (result.Ok)
        {
            SetStatus(successMessage, isError: false);
        }
        else
        {
            SetStatus(string.IsNullOrWhiteSpace(result.Error) ? "操作失败。" : result.Error, isError: true);
        }
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusNotification?.Invoke(message, isError);
        }
    }
}
