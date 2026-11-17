using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ContextAll.App.Controls;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E This Device view for local identity, download folder, appearance/accent overrides,
/// tray/window toggles, live background service status, usage guide entry, and host diagnostics.
/// </summary>
public partial class ThisDeviceView : UserControl
{
    private readonly ThisDeviceViewModel _vm;

    private Border? _statusBanner;
    private TextBlock? _statusText;
    private TextBlock? _deviceIdLabel;
    private TextBox? _nameInput;
    private TextBlock? _downloadDirLabel;
    private ExpressiveSwitch? _traySwitch;
    private ExpressiveSwitch? _nativeTitleBarSwitch;
    private Button? _autostartButton;
    private Button? _reconnectDaemonButton;
    private Border? _serviceStatusPill;
    private TextBlock? _serviceStatusBadgeText;
    private TextBlock? _serviceStatusDetailText;
    private ExpressiveGroupTile? _guideTile;
    private ExpressiveGroupTile? _aboutTile;
    private TextBlock? _aboutEntrySummaryText;
    private MaterialDialog _aboutDialog = new();
    private TextBlock? _aboutVersionText;
    private TextBlock? _aboutProtocolText;
    private TextBlock? _aboutOsDescriptionText;
    private TextBlock? _aboutOsVersionText;
    private TextBlock? _aboutCpuText;
    private TextBlock? _aboutDisplayServerText;
    private TextBlock? _aboutClipboardBackendText;
    private TextBlock? _aboutDeviceIdText;
    private TextBlock? _aboutCopyFeedbackText;
    private Button? _modeFollowButton;
    private Button? _modeLightButton;
    private Button? _modeDarkButton;
    private Button? _accentFollowButton;
    private Button? _accentBlueButton;
    private Button? _accentTealButton;
    private Button? _accentSunsetButton;
    private Button? _accentVioletButton;

    public ThisDeviceView()
        : this(new ThisDeviceViewModel(
            IdleLocalChannel.Shared,
            new LinuxDaemonController(),
            new ShellPreferences(),
            _ => { }))
    {
    }

    public ThisDeviceView(ThisDeviceViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        _vm.FolderPicker ??= OpenPortalFolderPickerAsync;

        AvaloniaXamlLoader.Load(this);
        ResolveNamedParts();

        if (_traySwitch is not null)
        {
            _traySwitch.Toggled += enabled => _vm.SetTrayEnabled(enabled);
        }

        if (_nativeTitleBarSwitch is not null)
        {
            _nativeTitleBarSwitch.Toggled += useNative => _vm.SetUseNativeTitleBar(useNative);
        }

        if (_guideTile is not null)
        {
            _guideTile.Activated += () => OpenGuideRequested?.Invoke();
        }

        if (_aboutTile is not null)
        {
            _aboutTile.Activated += () => _vm.OpenAboutDialog();
        }

        _aboutDialog.PrimaryActionClicked += (_, _) => _vm.CloseAboutDialog();
        _aboutDialog.Canceled += (_, _) => _vm.CloseAboutDialog();

        Md3eResourceLookup.TrackThemeChanges(this, UpdateBannerColors);
        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public event Action? OpenGuideRequested;

    public AppearanceModeOverride SelectedAppearanceMode => _vm.AppearanceModeOverride;
    public AccentSourceOverride SelectedAccentSource => _vm.AccentSourceOverride;
    public MaterialDialog AboutDialog => _aboutDialog;
    public bool IsAboutDialogOpen => _aboutDialog.IsOpen;

    public void SelectAppearanceMode(AppearanceModeOverride mode) =>
        _vm.SetAppearanceModeOverride(mode);

    public void SelectAccentSource(AccentSourceOverride accent) =>
        _vm.SetAccentSourceOverride(accent);

    private void ResolveNamedParts()
    {
        _statusBanner = this.FindControl<Border>("StatusBanner");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _deviceIdLabel = this.FindControl<TextBlock>("DeviceIdLabel");
        _nameInput = this.FindControl<TextBox>("NameInput");
        _downloadDirLabel = this.FindControl<TextBlock>("DownloadDirLabel");
        _traySwitch = this.FindControl<ExpressiveSwitch>("TraySwitch");
        _nativeTitleBarSwitch = this.FindControl<ExpressiveSwitch>("NativeTitleBarSwitch");
        _autostartButton = this.FindControl<Button>("AutostartButton");
        _reconnectDaemonButton = this.FindControl<Button>("ReconnectDaemonButton");
        _serviceStatusPill = this.FindControl<Border>("ServiceStatusPill");
        _serviceStatusBadgeText = this.FindControl<TextBlock>("ServiceStatusBadgeText");
        _serviceStatusDetailText = this.FindControl<TextBlock>("ServiceStatusDetailText");
        _guideTile = this.FindControl<ExpressiveGroupTile>("GuideTile");
        _aboutTile = this.FindControl<ExpressiveGroupTile>("AboutTile");
        _aboutEntrySummaryText = this.FindControl<TextBlock>("AboutEntrySummaryText");
        if (this.FindControl<MaterialDialog>("AboutDialogControl") is { } aboutDialog)
        {
            _aboutDialog = aboutDialog;
        }
        _aboutVersionText = this.FindControl<TextBlock>("AboutVersionText");
        _aboutProtocolText = this.FindControl<TextBlock>("AboutProtocolText");
        _aboutOsDescriptionText = this.FindControl<TextBlock>("AboutOsDescriptionText");
        _aboutOsVersionText = this.FindControl<TextBlock>("AboutOsVersionText");
        _aboutCpuText = this.FindControl<TextBlock>("AboutCpuText");
        _aboutDisplayServerText = this.FindControl<TextBlock>("AboutDisplayServerText");
        _aboutClipboardBackendText = this.FindControl<TextBlock>("AboutClipboardBackendText");
        _aboutDeviceIdText = this.FindControl<TextBlock>("AboutDeviceIdText");
        _aboutCopyFeedbackText = this.FindControl<TextBlock>("AboutCopyFeedbackText");
        _modeFollowButton = this.FindControl<Button>("ModeFollowButton");
        _modeLightButton = this.FindControl<Button>("ModeLightButton");
        _modeDarkButton = this.FindControl<Button>("ModeDarkButton");
        _accentFollowButton = this.FindControl<Button>("AccentFollowButton");
        _accentBlueButton = this.FindControl<Button>("AccentBlueButton");
        _accentTealButton = this.FindControl<Button>("AccentTealButton");
        _accentSunsetButton = this.FindControl<Button>("AccentSunsetButton");
        _accentVioletButton = this.FindControl<Button>("AccentVioletButton");
    }

    private void OnNameInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_nameInput is not null)
        {
            _vm.DisplayNameDraft = _nameInput.Text ?? string.Empty;
        }
    }

    private async void OnSaveNameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.SaveDisplayNameAsync();

    private async void OnChooseFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.ChooseDownloadDirectoryAsync();

    private async void OnAutostartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.EnableAutostartAsync();

    private async void OnStartDaemonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.StartDaemonAsync();

    private void OnOpenGuideClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenGuideRequested?.Invoke();

    private void OnOpenAboutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.OpenAboutDialog();

    private async void OnCopyDiagnosticsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_vm.BuildDiagnosticsSummary());
            _vm.NotifyDiagnosticsCopied();
        }
    }

    private void OnModeFollowClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAppearanceModeOverride(AppearanceModeOverride.FollowSystem);

    private void OnModeLightClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAppearanceModeOverride(AppearanceModeOverride.Light);

    private void OnModeDarkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAppearanceModeOverride(AppearanceModeOverride.Dark);

    private void OnAccentFollowClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAccentSourceOverride(AccentSourceOverride.FollowSystem);

    private void OnAccentBlueClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAccentSourceOverride(AccentSourceOverride.BrandBlue);

    private void OnAccentTealClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAccentSourceOverride(AccentSourceOverride.ForestTeal);

    private void OnAccentSunsetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAccentSourceOverride(AccentSourceOverride.SunsetTerracotta);

    private void OnAccentVioletClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.SetAccentSourceOverride(AccentSourceOverride.VioletIndigo);

    private async Task<string?> OpenPortalFolderPickerAsync(CancellationToken ct)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { CanPickFolder: true } provider)
        {
            return null;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择文件保存目录",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void Refresh()
    {
        if (_deviceIdLabel is not null)
        {
            _deviceIdLabel.Text = string.IsNullOrWhiteSpace(_vm.LocalDeviceId)
                ? "设备编号：等待连接后台服务"
                : $"设备编号：{_vm.LocalDeviceId}";
        }

        if (_nameInput is not null && !string.Equals(_nameInput.Text ?? string.Empty, _vm.DisplayNameDraft, StringComparison.Ordinal))
        {
            _nameInput.Text = _vm.DisplayNameDraft;
        }

        if (_downloadDirLabel is not null)
        {
            _downloadDirLabel.Text = string.IsNullOrWhiteSpace(_vm.ResolvedDownloadDirectory)
                ? "默认保存目录：~/Downloads"
                : $"当前目录：{_vm.ResolvedDownloadDirectory}";
        }

        if (_traySwitch is not null)
        {
            _traySwitch.IsChecked = _vm.TrayEnabled;
        }

        if (_nativeTitleBarSwitch is not null)
        {
            _nativeTitleBarSwitch.IsChecked = _vm.UseNativeTitleBar;
        }

        if (_autostartButton is not null)
        {
            _autostartButton.Content = _vm.AutostartActionLabel;
        }

        if (_serviceStatusBadgeText is not null)
        {
            _serviceStatusBadgeText.Text = _vm.ServiceStatusLabel;
        }

        if (_serviceStatusDetailText is not null)
        {
            _serviceStatusDetailText.Text = _vm.ServiceStatusDetail;
        }

        if (_reconnectDaemonButton is not null)
        {
            _reconnectDaemonButton.IsVisible = !_vm.IsDaemonConnected;
            _reconnectDaemonButton.IsEnabled = _vm.CanStartDaemon;
            _reconnectDaemonButton.Content = _vm.IsStartingDaemon ? "正在启动…" : "启动后台服务";
        }

        if (_aboutEntrySummaryText is not null)
        {
            _aboutEntrySummaryText.Text = _vm.AboutEntrySummary;
        }

        _aboutDialog.IsOpen = _vm.IsAboutDialogOpen;
        if (_aboutVersionText is not null)
        {
            _aboutVersionText.Text = _vm.AppVersionLabel;
        }
        if (_aboutProtocolText is not null)
        {
            _aboutProtocolText.Text = _vm.ProtocolStackDetail;
        }
        if (_aboutOsDescriptionText is not null)
        {
            _aboutOsDescriptionText.Text = _vm.HostOsDescription;
        }
        if (_aboutOsVersionText is not null)
        {
            _aboutOsVersionText.Text = _vm.HostOsVersion;
        }
        if (_aboutCpuText is not null)
        {
            _aboutCpuText.Text = _vm.HostCpuSummary;
        }
        if (_aboutDisplayServerText is not null)
        {
            _aboutDisplayServerText.Text = _vm.DisplayServerLabel;
        }
        if (_aboutClipboardBackendText is not null)
        {
            _aboutClipboardBackendText.Text = $"{_vm.ClipboardBackendLabel} ({_vm.ClipboardBackendCode})";
        }
        if (_aboutDeviceIdText is not null)
        {
            _aboutDeviceIdText.Text = string.IsNullOrWhiteSpace(_vm.LocalDeviceId) ? "未连接" : _vm.LocalDeviceId;
        }
        if (_aboutCopyFeedbackText is not null)
        {
            _aboutCopyFeedbackText.IsVisible = !string.IsNullOrWhiteSpace(_vm.DiagnosticsCopiedFeedback);
            _aboutCopyFeedbackText.Text = _vm.DiagnosticsCopiedFeedback ?? string.Empty;
        }

        ApplySelectionClass(_modeFollowButton, _vm.AppearanceModeOverride == AppearanceModeOverride.FollowSystem);
        ApplySelectionClass(_modeLightButton, _vm.AppearanceModeOverride == AppearanceModeOverride.Light);
        ApplySelectionClass(_modeDarkButton, _vm.AppearanceModeOverride == AppearanceModeOverride.Dark);

        ApplySelectionClass(_accentFollowButton, _vm.AccentSourceOverride == AccentSourceOverride.FollowSystem);
        ApplySelectionClass(_accentBlueButton, _vm.AccentSourceOverride == AccentSourceOverride.BrandBlue);
        ApplySelectionClass(_accentTealButton, _vm.AccentSourceOverride == AccentSourceOverride.ForestTeal);
        ApplySelectionClass(_accentSunsetButton, _vm.AccentSourceOverride == AccentSourceOverride.SunsetTerracotta);
        ApplySelectionClass(_accentVioletButton, _vm.AccentSourceOverride == AccentSourceOverride.VioletIndigo);

        if (_statusBanner is not null && _statusText is not null)
        {
            _statusBanner.IsVisible = !string.IsNullOrWhiteSpace(_vm.StatusMessage);
            _statusText.Text = _vm.StatusMessage ?? string.Empty;
        }

        UpdateBannerColors();
    }

    private void UpdateBannerColors()
    {
        if (_statusBanner is not null && _statusText is not null)
        {
            _statusBanner.Background = _vm.StatusIsError
                ? Md3eResourceLookup.ResolveBrush(this, "ErrorContainer", static p => p.ErrorContainer)
                : Md3eResourceLookup.ResolveBrush(this, "SecondaryContainer", static p => p.SecondaryContainer);
            _statusText.Foreground = _vm.StatusIsError
                ? Md3eResourceLookup.ResolveBrush(this, "OnErrorContainer", static p => p.OnErrorContainer)
                : Md3eResourceLookup.ResolveBrush(this, "OnSecondaryContainer", static p => p.OnSecondaryContainer);
        }

        if (_serviceStatusPill is not null && _serviceStatusBadgeText is not null)
        {
            _serviceStatusPill.Background = _vm.IsDaemonConnected
                ? Md3eResourceLookup.ResolveBrush(this, "SecondaryContainer", static p => p.SecondaryContainer)
                : Md3eResourceLookup.ResolveBrush(this, "SurfaceContainerHighest", static p => p.SurfaceContainerHighest);
            _serviceStatusBadgeText.Foreground = _vm.IsDaemonConnected
                ? Md3eResourceLookup.ResolveBrush(this, "OnSecondaryContainer", static p => p.OnSecondaryContainer)
                : Md3eResourceLookup.ResolveBrush(this, "OnSurfaceVariant", static p => p.OnSurfaceVariant);
        }
    }

    private static void ApplySelectionClass(Button? button, bool active)
    {
        if (button is null)
        {
            return;
        }

        button.Classes.Remove("FilledTonal");
        button.Classes.Remove("Outlined");
        button.Classes.Add(active ? "FilledTonal" : "Outlined");
    }
}
