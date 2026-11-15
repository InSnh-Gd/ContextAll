using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ContextAll.App.Controls;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E This Device view for local identity, download folder, appearance/accent overrides,
/// tray/SSD toggles, and autostart installation. Never shows reset actions while connected.
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
            new LocalDaemonConnection(),
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

        Md3eResourceLookup.TrackThemeChanges(this, UpdateBannerColors);
        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public AppearanceModeOverride SelectedAppearanceMode => _vm.AppearanceModeOverride;
    public AccentSourceOverride SelectedAccentSource => _vm.AccentSourceOverride;

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
        if (_statusBanner is null || _statusText is null)
        {
            return;
        }

        _statusBanner.Background = _vm.StatusIsError
            ? Md3eResourceLookup.ResolveBrush(this, "ErrorContainer", static p => p.ErrorContainer)
            : Md3eResourceLookup.ResolveBrush(this, "SecondaryContainer", static p => p.SecondaryContainer);
        _statusText.Foreground = _vm.StatusIsError
            ? Md3eResourceLookup.ResolveBrush(this, "OnErrorContainer", static p => p.OnErrorContainer)
            : Md3eResourceLookup.ResolveBrush(this, "OnSecondaryContainer", static p => p.OnSecondaryContainer);
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
