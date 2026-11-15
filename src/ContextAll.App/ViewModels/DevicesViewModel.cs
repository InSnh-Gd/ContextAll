using System.Text;
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
    }

    public event Action<string, bool>? StatusNotification;

    public IReadOnlyList<DeviceSnapshotPayload> TrustedDevices =>
        [.. _state.Devices.Where(d => d.Trusted).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<DeviceSnapshotPayload> DiscoveredDevices =>
        [.. _state.Devices.Where(d => !d.Trusted).OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    public bool HasTrustedDevices => TrustedDevices.Count > 0;
    public bool HasDiscoveredDevices => DiscoveredDevices.Count > 0;
    public bool HasAnyDevices => _state.Devices.Count > 0;

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

    public IReadOnlyList<LocalTransferHistoryEntry> SelectedDeviceHistory =>
        SelectedDeviceId is null
            ? []
            : [.. _state.History
                .Where(h => string.Equals(h.DeviceId, SelectedDeviceId, StringComparison.Ordinal))
                .OrderByDescending(h => h.EndedAt)];

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
        OnPropertyChanged(nameof(HasTrustedDevices));
        OnPropertyChanged(nameof(HasDiscoveredDevices));
        OnPropertyChanged(nameof(HasAnyDevices));
        OnPropertyChanged(nameof(SelectedDeviceId));
        OnPropertyChanged(nameof(ConfirmingUnpairDeviceId));
        OnPropertyChanged(nameof(PairingPrompt));
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
            SetStatus("请输入要推送的纯文本内容。", isError: true);
            return false;
        }

        if (IsClipboardTooLarge)
        {
            SetStatus("剪贴板文本超过 1 MiB 限制，无法推送。", isError: true);
            return false;
        }

        var result = await _client.PushClipboardTextAsync(_clipboardDraftText, ct);
        if (result.Ok)
        {
            IsClipboardModalOpen = false;
            ClipboardDraftText = string.Empty;
        }

        ApplyResult(result, "已推送剪贴板文本。");
        return result.Ok;
    }

    private void OnClientStateChanged(LocalShellState next) => DispatchToUi(() => ApplyState(next));

    private void NotifyLayoutProperties()
    {
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(SelectedDeviceHistory));
        OnPropertyChanged(nameof(HasSelectedDeviceHistory));
        OnPropertyChanged(nameof(SelectedDeviceClipboardDisabled));
        OnPropertyChanged(nameof(ClipboardBroadcastClarification));
        OnPropertyChanged(nameof(ShowListPane));
        OnPropertyChanged(nameof(ShowDetailPane));
        OnPropertyChanged(nameof(CanNavigateBack));
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
