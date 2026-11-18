using System.Reflection;
using System.Runtime.InteropServices;
using ContextAll.App.Platform;
using ContextAll.Ipc;

namespace ContextAll.App.ViewModels;

/// <summary>View model for the This Device (本机) section, preferences, live service status, and host diagnostics.</summary>
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
    private bool _isStartingDaemon;
    private bool _isAboutDialogOpen;
    private string? _diagnosticsCopiedFeedback;

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

        AppVersionLabel = FormatAppVersion(Assembly.GetExecutingAssembly().GetName().Version);
        HostOsVersion = Environment.OSVersion.VersionString;
        HostOsDescription = RuntimeInformation.OSDescription;
        HostArchitecture = RuntimeInformation.OSArchitecture.ToString();
        HostProcessorCount = Environment.ProcessorCount;
        DisplayServerLabel = ResolveDisplayServerLabel(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            OperatingSystem.IsWindows());

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
    public bool IsAppearanceFollowSystem => AppearanceModeOverride == AppearanceModeOverride.FollowSystem;
    public bool IsAppearanceLight => AppearanceModeOverride == AppearanceModeOverride.Light;
    public bool IsAppearanceDark => AppearanceModeOverride == AppearanceModeOverride.Dark;
    public AccentSourceOverride AccentSourceOverride => _preferences.EffectiveAccentSource;
    public bool IsAccentFollowSystem => AccentSourceOverride == AccentSourceOverride.FollowSystem;
    public bool IsAccentBrandBlue => AccentSourceOverride == AccentSourceOverride.BrandBlue;
    public bool IsAccentForestTeal => AccentSourceOverride == AccentSourceOverride.ForestTeal;
    public bool IsAccentSunsetTerracotta => AccentSourceOverride == AccentSourceOverride.SunsetTerracotta;
    public bool IsAccentVioletIndigo => AccentSourceOverride == AccentSourceOverride.VioletIndigo;
    public bool IsRunningFromAppImage => _daemon.IsRunningFromAppImage;
    public string AutostartActionLabel => IsRunningFromAppImage ? "安装 AppImage 并启用开机自启" : "启用 systemd 用户开机自启";

    public LocalConnectionStatus ConnectionStatus => _state.ConnectionStatus;
    public bool IsDaemonConnected => _state.Connected;

    public bool IsStartingDaemon
    {
        get => _isStartingDaemon;
        private set
        {
            if (SetField(ref _isStartingDaemon, value))
            {
                OnPropertyChanged(nameof(CanStartDaemon));
            }
        }
    }

    public bool CanStartDaemon => !_state.Connected && !_isStartingDaemon;

    public string ServiceStatusLabel => _state.ConnectionStatus switch
    {
        LocalConnectionStatus.Connected => "已连接",
        LocalConnectionStatus.Connecting => "正在连接…",
        _ => "未连接",
    };

    public string ServiceStatusDetail => _state.ConnectionStatus switch
    {
        LocalConnectionStatus.Connected => $"后台服务运行中 · 剪贴板：{ClipboardBackendLabel}",
        LocalConnectionStatus.Connecting => $"正在连接后台服务… · 剪贴板：{ClipboardBackendLabel}",
        _ => $"后台服务未连接 · 剪贴板：{ClipboardBackendLabel}",
    };

    public string ClipboardBackendCode => _state.ClipboardBackend;

    public string ClipboardBackendLabel => _state.ClipboardBackend switch
    {
        "wayland" => "Wayland 原生",
        "x11" => "X11 回退",
        "windows" => "Windows 原生",
        _ => "未就绪",
    };

    public string AppVersionLabel { get; }

    public string ProtocolSummary =>
        _state.QuicPort > 0 ? $"QUIC · UDP {_state.QuicPort}" : "QUIC · UDP 42461";

    public string ProtocolStackDetail =>
        _state.QuicPort > 0
            ? $"QUIC TLS 1.3 · ECDSA P-256 · SHA-256（端口 UDP {_state.QuicPort}）"
            : "QUIC TLS 1.3 · ECDSA P-256 · SHA-256（默认端口 UDP 42461）";

    public string AboutEntrySummary => $"{AppVersionLabel} · {ProtocolSummary}";

    public string HostOsVersion { get; }
    public string HostOsDescription { get; }
    public string HostArchitecture { get; }
    public int HostProcessorCount { get; }
    public string HostCpuSummary => $"{HostArchitecture} · {HostProcessorCount} 核处理器";
    public string DisplayServerLabel { get; }

    public bool IsAboutDialogOpen
    {
        get => _isAboutDialogOpen;
        private set => SetField(ref _isAboutDialogOpen, value);
    }

    public string? DiagnosticsCopiedFeedback
    {
        get => _diagnosticsCopiedFeedback;
        private set => SetField(ref _diagnosticsCopiedFeedback, value);
    }

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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

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
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(IsDaemonConnected));
        OnPropertyChanged(nameof(CanStartDaemon));
        OnPropertyChanged(nameof(ServiceStatusLabel));
        OnPropertyChanged(nameof(ServiceStatusDetail));
        OnPropertyChanged(nameof(ClipboardBackendCode));
        OnPropertyChanged(nameof(ClipboardBackendLabel));
        OnPropertyChanged(nameof(ProtocolSummary));
        OnPropertyChanged(nameof(ProtocolStackDetail));
        OnPropertyChanged(nameof(AboutEntrySummary));
    }

    public void ApplyWindowPreferences(ShellPreferences prefs)
    {
        _preferences = prefs;
        OnPropertyChanged(nameof(TrayEnabled));
        OnPropertyChanged(nameof(UseNativeTitleBar));
        NotifyAppearanceOverrides();
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

    public void OpenAboutDialog()
    {
        DiagnosticsCopiedFeedback = null;
        IsAboutDialogOpen = true;
    }

    public void CloseAboutDialog()
    {
        IsAboutDialogOpen = false;
        DiagnosticsCopiedFeedback = null;
    }

    public string BuildDiagnosticsSummary()
    {
        var deviceId = string.IsNullOrWhiteSpace(_state.LocalDeviceId) ? "未连接" : _state.LocalDeviceId;
        var displayName = string.IsNullOrWhiteSpace(_state.LocalDisplayName) ? "未命名" : _state.LocalDisplayName;
        return string.Join(
            Environment.NewLine,
            [
                $"ContextAll {AppVersionLabel}",
                $"本机名称：{displayName}",
                $"设备编号：{deviceId}",
                $"传输协议：{ProtocolStackDetail}",
                $"操作系统：{HostOsDescription}",
                $"系统版本：{HostOsVersion}",
                $"处理器：{HostCpuSummary}",
                $"显示会话：{DisplayServerLabel}",
                $"剪贴板后端：{ClipboardBackendLabel} ({ClipboardBackendCode})",
                $"后台服务：{ServiceStatusLabel}",
            ]);
    }

    public void NotifyDiagnosticsCopied()
    {
        DiagnosticsCopiedFeedback = "已复制到剪贴板";
        SetStatus("已复制本机与版本信息到剪贴板。", isError: false);
    }

    public void NotifyDeviceIdCopied() =>
        SetStatus("已复制设备编号。", isError: false);

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
            SetStatus($"已更新文件保存目录：{folder}", isError: false);
            return true;
        }

        SetStatus(string.IsNullOrWhiteSpace(result.Error) ? "修改文件保存目录失败。" : result.Error, isError: true);
        return false;
    }

    public async Task<bool> EnableAutostartAsync(CancellationToken ct = default)
    {
        var result = await _daemon.EnableAutostartAsync(ct);
        SetStatus(
            result.Message ?? (result.Succeeded ? "已启用开机自启。" : "启用开机自启失败。"),
            isError: !result.Succeeded);
        return result.Succeeded;
    }

    public async Task<bool> StartDaemonAsync(CancellationToken ct = default)
    {
        if (_isStartingDaemon)
        {
            return false;
        }

        IsStartingDaemon = true;
        try
        {
            var result = await _daemon.StartDaemonAsync(ct);
            if (result.Outcome == DaemonStartOutcome.Started)
            {
                _client.RetryNow();
                SetStatus("后台服务已启动，正在建立连接。", isError: false);
                return true;
            }

            var fallback = result.Outcome == DaemonStartOutcome.CorruptState
                ? "后台服务未能启动，本机身份凭证或配置文件可能已损坏。"
                : "启动后台服务失败，请稍后再试。";
            SetStatus(string.IsNullOrWhiteSpace(result.Message) ? fallback : result.Message, isError: true);
            return false;
        }
        finally
        {
            IsStartingDaemon = false;
        }
    }

    private static string FormatAppVersion(Version? version)
    {
        if (version is null)
        {
            return "v1.0.0";
        }

        var build = version.Build >= 0 ? version.Build : 0;
        return $"v{version.Major}.{version.Minor}.{build}";
    }

    public static string ResolveDisplayServerLabel(string? waylandDisplay, string? x11Display, bool isWindows = false) =>
        DisplayServerLabels.Resolve(waylandDisplay, x11Display, isWindows);

    private void UpdatePreferences(ShellPreferences next)
    {
        _preferences = next;
        _savePreferences(next);
        OnPropertyChanged(nameof(TrayEnabled));
        OnPropertyChanged(nameof(UseNativeTitleBar));
        NotifyAppearanceOverrides();
        PreferencesChanged?.Invoke(next);
    }

    private void NotifyAppearanceOverrides()
    {
        OnPropertyChanged(nameof(AppearanceModeOverride));
        OnPropertyChanged(nameof(IsAppearanceFollowSystem));
        OnPropertyChanged(nameof(IsAppearanceLight));
        OnPropertyChanged(nameof(IsAppearanceDark));
        OnPropertyChanged(nameof(AccentSourceOverride));
        OnPropertyChanged(nameof(IsAccentFollowSystem));
        OnPropertyChanged(nameof(IsAccentBrandBlue));
        OnPropertyChanged(nameof(IsAccentForestTeal));
        OnPropertyChanged(nameof(IsAccentSunsetTerracotta));
        OnPropertyChanged(nameof(IsAccentVioletIndigo));
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
        OnPropertyChanged(nameof(HasStatusMessage));
    }
}
