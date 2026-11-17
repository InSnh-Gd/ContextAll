using ContextAll.App.Controls;
using ContextAll.App.Platform;
using ContextAll.Ipc;

namespace ContextAll.App.ViewModels;

/// <summary>Top-level MD3E shell coordinator owning all four sections, onboarding, and corrupt-state recovery.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly ILocalDaemonClient _client;
    private readonly IDaemonController _daemon;
    private readonly Action<ShellPreferences> _savePreferences;
    private ShellPreferences _preferences;
    private bool _autoGuideTriggered;
    private ShellSection _selectedSection = ShellSection.Devices;
    private double _width = ShellLayout.TwoColumnWidth;
    private bool _isRailExpanded;
    private LocalShellState _state = LocalShellState.Empty;
    private bool _isOnboardingBusy;
    private string? _onboardingFeedback;
    private bool _isCorruptStateDialogOpen;
    private bool _isCorruptRecovering;
    private string? _corruptStateError;

    public ShellViewModel(
        ILocalDaemonClient client,
        IDaemonController daemon,
        ShellPreferences initialPreferences,
        Action<ShellPreferences> savePreferences,
        Action<Action>? dispatchToUi = null)
        : base(dispatchToUi)
    {
        _client = client;
        _daemon = daemon;
        _preferences = initialPreferences;
        _autoGuideTriggered = initialPreferences.GuideSeen;
        _savePreferences = savePreferences;
        _state = client.CurrentState;

        Devices = new DevicesViewModel(client, dispatchToUi);
        Connection = new ConnectionViewModel(
            client,
            seen => UpdatePreferences(_preferences with { GuideSeen = seen }),
            dispatchToUi);
        Transfers = new TransfersViewModel(client, dispatchToUi: dispatchToUi);
        ThisDevice = new ThisDeviceViewModel(
            client,
            daemon,
            initialPreferences,
            prefs =>
            {
                _preferences = prefs;
                _savePreferences(prefs);
                PreferencesChanged?.Invoke(prefs);
            },
            dispatchToUi);

        Devices.StatusNotification += (msg, _) => RequestSnackbar(msg);
        Transfers.NoticeNotification += (msg, _) => RequestSnackbar(msg);

        _client.StateChanged += next => DispatchToUi(() => ApplyState(next));
        MaybeAutoOpenGuide();
    }

    public event Action<ShellPreferences>? PreferencesChanged;

    public event Action<string, string?, Action?>? SnackbarRequested;

    public DevicesViewModel Devices { get; }
    public ConnectionViewModel Connection { get; }
    public TransfersViewModel Transfers { get; }
    public ThisDeviceViewModel ThisDevice { get; }

    public ShellSection SelectedSection
    {
        get => _selectedSection;
        set => SetField(ref _selectedSection, value);
    }

    public double Width
    {
        get => _width;
        private set
        {
            if (SetField(ref _width, value))
            {
                OnPropertyChanged(nameof(IsWideLayout));
            }
        }
    }

    public bool IsWideLayout => ShellLayout.TwoColumns(_width);

    public bool IsRailExpanded
    {
        get => _isRailExpanded;
        set => SetField(ref _isRailExpanded, value);
    }

    public LocalConnectionStatus ConnectionStatus => _state.ConnectionStatus;
    public bool Connected => _state.Connected;
    public string DaemonStatusLabel => _state.Connected ? "已连接" : "未连接";
    public bool ShowDaemonOnboarding => !_state.Connected;
    public string LocalDisplayName => _state.LocalDisplayName;
    public string? LastDisconnectReason => _state.LastDisconnectReason;
    public bool HasLastDisconnectReason => !string.IsNullOrWhiteSpace(_state.LastDisconnectReason);
    public string LastDisconnectReasonText => string.IsNullOrWhiteSpace(_state.LastDisconnectReason)
        ? string.Empty
        : $"上次断开原因：{_state.LastDisconnectReason}";

    public bool IsOnboardingBusy
    {
        get => _isOnboardingBusy;
        private set => SetField(ref _isOnboardingBusy, value);
    }

    public string? OnboardingFeedback
    {
        get => _onboardingFeedback;
        private set => SetField(ref _onboardingFeedback, value);
    }

    public bool IsCorruptStateDialogOpen
    {
        get => _isCorruptStateDialogOpen;
        private set => SetField(ref _isCorruptStateDialogOpen, value);
    }

    public string DefaultCorruptAction => "cancel";

    public bool IsCorruptRecovering
    {
        get => _isCorruptRecovering;
        private set => SetField(ref _isCorruptRecovering, value);
    }

    public string? CorruptStateError
    {
        get => _corruptStateError;
        private set => SetField(ref _corruptStateError, value);
    }

    public void ApplyWidth(double width)
    {
        Width = width;
        Devices.ApplyWidth(width);
    }

    public void ApplyState(LocalShellState next)
    {
        _state = next;
        if (next.Connected)
        {
            IsCorruptStateDialogOpen = false;
            OnboardingFeedback = null;
        }

        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(Connected));
        OnPropertyChanged(nameof(DaemonStatusLabel));
        OnPropertyChanged(nameof(ShowDaemonOnboarding));
        OnPropertyChanged(nameof(LocalDisplayName));
        OnPropertyChanged(nameof(LastDisconnectReason));
        OnPropertyChanged(nameof(HasLastDisconnectReason));
        OnPropertyChanged(nameof(LastDisconnectReasonText));

        MaybeAutoOpenGuide();
    }

    /// <summary>Raises a non-blocking snackbar toast notification for the active shell host.</summary>
    public void RequestSnackbar(string message, string? actionText = null, Action? actionCallback = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            SnackbarRequested?.Invoke(message, actionText, actionCallback);
        }
    }

    /// <summary>Executes the navigation rail primary Extended FAB action (send-files).</summary>
    public void TriggerRailFab()
    {
        if (!Connected)
        {
            RequestSnackbar("后台服务尚未连接，请先启动服务。");
            return;
        }

        if (Devices.TrustedDevices.Count == 0)
        {
            RequestSnackbar("请先配对至少一台设备再发送文件。", "去连接", () => SelectedSection = ShellSection.Connection);
            return;
        }

        var targetId = Devices.SelectedDevice is { Trusted: true } selected
            ? selected.DeviceId
            : Devices.TrustedDevices[0].DeviceId;
        NavigateToSendFilesForPeer(targetId);
    }

    public void NavigateToSendFilesForPeer(string deviceId)
    {
        Transfers.SelectTargetPeer(deviceId);
        SelectedSection = ShellSection.Transfers;
    }

    public async Task<bool> StartDaemonExplicitAsync(CancellationToken ct = default)
    {
        if (IsOnboardingBusy)
        {
            return false;
        }

        IsOnboardingBusy = true;
        OnboardingFeedback = null;
        try
        {
            var result = await _daemon.StartDaemonAsync(ct);
            switch (result.Outcome)
            {
                case DaemonStartOutcome.Started:
                    _client.RetryNow();
                    OnboardingFeedback = "后台服务已启动，正在连接…";
                    return true;
                case DaemonStartOutcome.CorruptState:
                    CorruptStateError = result.Message ?? "本机身份信息或配置文件已损坏，是否重置并重新生成？";
                    IsCorruptStateDialogOpen = true;
                    return false;
                default:
                    OnboardingFeedback = result.Message ?? "后台服务启动失败。";
                    return false;
            }
        }
        finally
        {
            IsOnboardingBusy = false;
        }
    }

    public async Task<bool> EnableAutostartExplicitAsync(CancellationToken ct = default)
    {
        if (IsOnboardingBusy)
        {
            return false;
        }

        IsOnboardingBusy = true;
        try
        {
            var res = await _daemon.EnableAutostartAsync(ct);
            OnboardingFeedback = res.Message ?? (res.Succeeded ? "已启用开机自启。" : "启用开机自启失败。");
            return res.Succeeded;
        }
        finally
        {
            IsOnboardingBusy = false;
        }
    }

    public void CancelCorruptReset()
    {
        IsCorruptStateDialogOpen = false;
        CorruptStateError = null;
    }

    public async Task<bool> ConfirmResetAndStartAsync(CancellationToken ct = default)
    {
        if (IsCorruptRecovering)
        {
            return false;
        }

        IsCorruptRecovering = true;
        try
        {
            var reset = await _daemon.ResetIdentityAndSettingsAsync(ct);
            if (!reset.Succeeded)
            {
                CorruptStateError = reset.Message ?? "重置失败，未能重建本机身份。";
                return false;
            }

            var start = await _daemon.StartDaemonAsync(ct);
            if (start.Outcome != DaemonStartOutcome.Started)
            {
                CorruptStateError = start.Message ?? "身份已重置，但后台服务启动失败。";
                return false;
            }

            IsCorruptStateDialogOpen = false;
            CorruptStateError = null;
            _client.RetryNow();
            return true;
        }
        finally
        {
            IsCorruptRecovering = false;
        }
    }

    private void MaybeAutoOpenGuide()
    {
        if (_autoGuideTriggered || _preferences.GuideSeen || !_state.Connected)
        {
            return;
        }

        _autoGuideTriggered = true;
        SelectedSection = ShellSection.Connection;
        Connection.OpenGuide();
    }

    private void UpdatePreferences(ShellPreferences next)
    {
        _preferences = next;
        _savePreferences(next);
        ThisDevice.ApplyWindowPreferences(next);
        PreferencesChanged?.Invoke(next);
    }
}
