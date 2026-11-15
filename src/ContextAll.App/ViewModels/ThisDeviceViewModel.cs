using ContextAll.App.Platform;
using ContextAll.Ipc;

namespace ContextAll.App.ViewModels;

/// <summary>View model for the This Device (本机) section. Intentionally omits any reset action while connected.</summary>
public sealed class ThisDeviceViewModel : ViewModelBase
{
    private readonly ILocalDaemonClient _client;
    private readonly IDaemonController _daemon;
    private readonly Action<ShellPreferences> _savePreferences;
    private ShellPreferences _preferences;
    private LocalShellState _state = LocalShellState.Empty;
    private string _displayNameDraft = string.Empty;
    private string _lastSyncedDisplayName = string.Empty;
    private string? _statusMessage;
    private bool _statusIsError;

    public ThisDeviceViewModel(
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
        _savePreferences = savePreferences;
        _state = client.CurrentState;
        _lastSyncedDisplayName = _state.LocalDisplayName;
        _displayNameDraft = _state.LocalDisplayName;

        _client.StateChanged += next => DispatchToUi(() => ApplyState(next));
    }

    public event Action<ShellPreferences>? PreferencesChanged;

    public Func<CancellationToken, Task<string?>>? FolderPicker { get; set; }

    public string LocalDeviceId => _state.LocalDeviceId;

    public string DisplayNameDraft
    {
        get => _displayNameDraft;
        set => SetField(ref _displayNameDraft, value);
    }

    public string DownloadDirectory => _state.DownloadDirectory;
    public string ResolvedDownloadDirectory =>
        string.IsNullOrWhiteSpace(_state.ResolvedDownloadDirectory) ? _state.DownloadDirectory : _state.ResolvedDownloadDirectory;

    public bool TrayEnabled => _preferences.TrayEnabled;
    public bool UseNativeTitleBar => _preferences.UseNativeTitleBar;
    public AppearanceModeOverride AppearanceModeOverride => _preferences.EffectiveAppearanceMode;
    public AccentSourceOverride AccentSourceOverride => _preferences.EffectiveAccentSource;
    public bool IsRunningFromAppImage => _daemon.IsRunningFromAppImage;
    public string AutostartActionLabel => IsRunningFromAppImage ? "安装 AppImage 并启用开机自启" : "启用 systemd 用户开机自启";

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
        if (!string.Equals(_lastSyncedDisplayName, state.LocalDisplayName, StringComparison.Ordinal))
        {
            _lastSyncedDisplayName = state.LocalDisplayName;
            DisplayNameDraft = state.LocalDisplayName;
        }

        OnPropertyChanged(nameof(LocalDeviceId));
        OnPropertyChanged(nameof(DownloadDirectory));
        OnPropertyChanged(nameof(ResolvedDownloadDirectory));
    }

    public void ApplyWindowPreferences(ShellPreferences prefs)
    {
        _preferences = prefs;
        OnPropertyChanged(nameof(TrayEnabled));
        OnPropertyChanged(nameof(UseNativeTitleBar));
        OnPropertyChanged(nameof(AppearanceModeOverride));
        OnPropertyChanged(nameof(AccentSourceOverride));
    }

    public void SetAppearanceModeOverride(AppearanceModeOverride mode)
    {
        if (_preferences.EffectiveAppearanceMode == mode)
        {
            return;
        }

        UpdatePreferences(_preferences with
        {
            AppearanceModeOverride = mode == AppearanceModeOverride.FollowSystem ? null : mode,
        });
    }

    public void SetAccentSourceOverride(AccentSourceOverride accent)
    {
        if (_preferences.EffectiveAccentSource == accent)
        {
            return;
        }

        UpdatePreferences(_preferences with
        {
            AccentSourceOverride = accent == AccentSourceOverride.FollowSystem ? null : accent,
        });
    }

    public void SetTrayEnabled(bool enabled)
    {
        if (_preferences.TrayEnabled == enabled)
        {
            return;
        }

        UpdatePreferences(_preferences with { TrayEnabled = enabled });
    }

    public void SetUseNativeTitleBar(bool useNative)
    {
        if (_preferences.UseNativeTitleBar == useNative)
        {
            return;
        }

        UpdatePreferences(_preferences with { UseNativeTitleBar = useNative });
    }

    public async Task<bool> SaveDisplayNameAsync(CancellationToken ct = default)
    {
        var trimmed = _displayNameDraft.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            SetStatus("本机名称不能为空。", isError: true);
            return false;
        }

        var result = await _client.SetDisplayNameAsync(trimmed, ct);
        if (result.Ok)
        {
            _lastSyncedDisplayName = trimmed;
            DisplayNameDraft = trimmed;
            SetStatus("已保存本机名称。", isError: false);
            return true;
        }

        SetStatus(string.IsNullOrWhiteSpace(result.Error) ? "保存本机名称失败。" : result.Error, isError: true);
        return false;
    }

    public async Task<bool> ChooseDownloadDirectoryAsync(CancellationToken ct = default)
    {
        if (FolderPicker is null)
        {
            return false;
        }

        var folder = await FolderPicker(ct);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var result = await _client.SetDownloadDirectoryAsync(folder, ct);
        if (result.Ok)
        {
            SetStatus($"已更新下载目录：{folder}", isError: false);
            return true;
        }

        SetStatus(string.IsNullOrWhiteSpace(result.Error) ? "修改下载目录失败。" : result.Error, isError: true);
        return false;
    }

    public async Task<bool> EnableAutostartAsync(CancellationToken ct = default)
    {
        var result = await _daemon.EnableAutostartAsync(ct);
        SetStatus(
            result.Message ?? (result.Succeeded ? "已配置开机自启。" : "配置开机自启失败。"),
            isError: !result.Succeeded);
        return result.Succeeded;
    }

    private void UpdatePreferences(ShellPreferences next)
    {
        _preferences = next;
        _savePreferences(next);
        OnPropertyChanged(nameof(TrayEnabled));
        OnPropertyChanged(nameof(UseNativeTitleBar));
        OnPropertyChanged(nameof(AppearanceModeOverride));
        OnPropertyChanged(nameof(AccentSourceOverride));
        PreferencesChanged?.Invoke(next);
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
