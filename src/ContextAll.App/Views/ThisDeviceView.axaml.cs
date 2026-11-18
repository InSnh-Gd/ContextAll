using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ContextAll.App.Chrome;
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
    private readonly StackPanel[] _sections;

    private TextBlock? _deviceIdLabel;
    private TextBox? _nameInput;
    private TextBlock? _downloadDirLabel;
    private ExpressiveSwitch? _traySwitch;
    private ExpressiveSwitch? _nativeTitleBarSwitch;
    private TextBlock? _serviceStatusDetailText;
    private ExpressiveGroupTile? _nameTile;
    private ExpressiveGroupTile? _deviceIdTile;
    private ExpressiveGroupTile? _downloadDirTile;
    private ExpressiveGroupTile? _trayTile;
    private ExpressiveGroupTile? _nativeTitleBarTile;
    private ExpressiveGroupTile? _guideTile;
    private ExpressiveGroupTile? _aboutTile;
    private ExpressiveButtonGroup? _appearanceGroup;
    private ExpressiveButtonGroup? _accentGroup;
    private ExpressiveButtonGroup? _serviceActionGroup;
    private TextBlock? _aboutEntrySummaryText;
    private MaterialDialog _renameDialog = new();
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

        InitializeComponent();
        _sections = [IdentitySection, AppearanceSection, ServiceSection, WindowSection, HelpSection];
        Categories.ItemsSource = _sections.Select(section => ((TextBlock)section.Children[0]).Text).ToArray();
        Categories.SelectedIndex = 0;
        Categories.SelectionChanged += (_, _) =>
        {
            UpdateSections();
            SettingsScroll.ScrollToHome();
        };
        ResolveNamedParts();
        WireInteractiveTiles();
        WireButtonGroups();

        _renameDialog.PrimaryActionClicked += async (_, _) =>
        {
            if (await _vm.SaveDisplayNameAsync())
            {
                _renameDialog.IsOpen = false;
            }
        };
        _renameDialog.Canceled += (_, _) => _renameDialog.IsOpen = false;

        _aboutDialog.PrimaryActionClicked += (_, _) => _vm.CloseAboutDialog();
        _aboutDialog.Canceled += (_, _) => _vm.CloseAboutDialog();

        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public event Action? OpenGuideRequested;

    public void ApplyTitleBarStatus(TitleBarStatus status) => TitleBarDescription.Text = status switch
    {
        TitleBarStatus.Pending => "正在请求系统标题栏…",
        TitleBarStatus.System => "正在使用系统标题栏。",
        TitleBarStatus.Fallback => "当前桌面未提供系统标题栏，正在使用应用标题栏。",
        _ => "正在使用应用标题栏。",
    };

    public void ApplyResponsiveLayout(bool wide)
    {
        if (Categories.IsVisible == wide) return;
        if (wide && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Avalonia.Visual focused)
        {
            var focusedSection = Array.FindIndex(_sections, section => section.IsVisualAncestorOf(focused));
            if (focusedSection >= 0) Categories.SelectedIndex = focusedSection;
        }
        Categories.IsVisible = wide;
        UpdateSections();
    }

    private void UpdateSections()
    {
        for (var index = 0; index < _sections.Length; index++)
        {
            _sections[index].IsVisible = !Categories.IsVisible || Categories.SelectedIndex == index;
        }
    }

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
        _deviceIdLabel = this.FindControl<TextBlock>("DeviceIdLabel");
        _nameInput = this.FindControl<TextBox>("NameInput");
        _downloadDirLabel = this.FindControl<TextBlock>("DownloadDirLabel");
        _traySwitch = this.FindControl<ExpressiveSwitch>("TraySwitch");
        _nativeTitleBarSwitch = this.FindControl<ExpressiveSwitch>("NativeTitleBarSwitch");
        _serviceStatusDetailText = this.FindControl<TextBlock>("ServiceStatusDetailText");
        _nameTile = this.FindControl<ExpressiveGroupTile>("NameTile");
        _deviceIdTile = this.FindControl<ExpressiveGroupTile>("DeviceIdTile");
        _downloadDirTile = this.FindControl<ExpressiveGroupTile>("DownloadDirTile");
        _trayTile = this.FindControl<ExpressiveGroupTile>("TrayTile");
        _nativeTitleBarTile = this.FindControl<ExpressiveGroupTile>("NativeTitleBarTile");
        _guideTile = this.FindControl<ExpressiveGroupTile>("GuideTile");
        _aboutTile = this.FindControl<ExpressiveGroupTile>("AboutTile");
        _appearanceGroup = this.FindControl<ExpressiveButtonGroup>("AppearanceModeGroup");
        _accentGroup = this.FindControl<ExpressiveButtonGroup>("AccentSourceGroup");
        _serviceActionGroup = this.FindControl<ExpressiveButtonGroup>("ServiceActionGroup");
        _aboutEntrySummaryText = this.FindControl<TextBlock>("AboutEntrySummaryText");
        if (this.FindControl<MaterialDialog>("RenameDialogControl") is { } renameDialog)
        {
            _renameDialog = renameDialog;
        }
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
    }

    private void WireInteractiveTiles()
    {
        if (_nameTile is not null)
        {
            _nameTile.Activated += OpenRenameDialog;
        }

        if (_deviceIdTile is not null)
        {
            _deviceIdTile.Activated += () => _ = CopyDeviceIdAsync();
        }

        if (_downloadDirTile is not null)
        {
            _downloadDirTile.Activated += () => _ = _vm.ChooseDownloadDirectoryAsync();
        }

        if (_trayTile is not null)
        {
            _trayTile.Activated += () => _vm.SetTrayEnabled(!_vm.TrayEnabled);
        }

        if (_nativeTitleBarTile is not null)
        {
            _nativeTitleBarTile.Activated += () => _vm.SetUseNativeTitleBar(!_vm.UseNativeTitleBar);
        }

        if (_guideTile is not null)
        {
            _guideTile.Activated += () => OpenGuideRequested?.Invoke();
        }

        if (_aboutTile is not null)
        {
            _aboutTile.Activated += () => _vm.OpenAboutDialog();
        }
    }

    private void WireButtonGroups()
    {
        if (_appearanceGroup is not null)
        {
            _appearanceGroup.ItemClicked += id =>
            {
                var mode = id switch
                {
                    "light" => AppearanceModeOverride.Light,
                    "dark" => AppearanceModeOverride.Dark,
                    _ => AppearanceModeOverride.FollowSystem,
                };
                _vm.SetAppearanceModeOverride(mode);
            };
        }

        if (_accentGroup is not null)
        {
            _accentGroup.ItemClicked += id =>
            {
                var accent = id switch
                {
                    "blue" => AccentSourceOverride.BrandBlue,
                    "teal" => AccentSourceOverride.ForestTeal,
                    "sunset" => AccentSourceOverride.SunsetTerracotta,
                    "violet" => AccentSourceOverride.VioletIndigo,
                    _ => AccentSourceOverride.FollowSystem,
                };
                _vm.SetAccentSourceOverride(accent);
            };
        }

        if (_serviceActionGroup is not null)
        {
            _serviceActionGroup.ItemClicked += id =>
            {
                if (id == "autostart")
                {
                    _ = _vm.EnableAutostartAsync();
                    return;
                }

                if (id == "start" && _vm.CanStartDaemon)
                {
                    _ = _vm.StartDaemonAsync();
                }
            };
        }
    }

    private void OnNameInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_nameInput is not null)
        {
            _vm.DisplayNameDraft = _nameInput.Text ?? string.Empty;
        }
    }

    private void OnEditNameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        OpenRenameDialog();

    private async void OnCopyDeviceIdClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await CopyDeviceIdAsync();

    private async void OnCopyDiagnosticsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_vm.BuildDiagnosticsSummary());
            _vm.NotifyDiagnosticsCopied();
        }
    }

    private void OpenRenameDialog()
    {
        if (_nameInput is not null &&
            !string.Equals(_nameInput.Text ?? string.Empty, _vm.DisplayNameDraft, StringComparison.Ordinal))
        {
            _nameInput.Text = _vm.DisplayNameDraft;
        }

        _renameDialog.IsOpen = true;
    }

    private async Task CopyDeviceIdAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.LocalDeviceId))
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_vm.LocalDeviceId);
            _vm.NotifyDeviceIdCopied();
        }
    }

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
                ? "等待连接后台服务"
                : _vm.LocalDeviceId;
        }

        if (_nameInput is not null && !string.Equals(_nameInput.Text ?? string.Empty, _vm.DisplayNameDraft, StringComparison.Ordinal))
        {
            _nameInput.Text = _vm.DisplayNameDraft;
        }

        if (_downloadDirLabel is not null)
        {
            _downloadDirLabel.Text = string.IsNullOrWhiteSpace(_vm.ResolvedDownloadDirectory)
                ? "~/Downloads"
                : _vm.ResolvedDownloadDirectory;
        }

        if (_traySwitch is not null)
        {
            _traySwitch.IsChecked = _vm.TrayEnabled;
        }

        if (_nativeTitleBarSwitch is not null)
        {
            _nativeTitleBarSwitch.IsChecked = _vm.UseNativeTitleBar;
        }

        if (_serviceStatusDetailText is not null)
        {
            _serviceStatusDetailText.Text = _vm.ServiceStatusDetail;
        }

        if (_aboutEntrySummaryText is not null)
        {
            _aboutEntrySummaryText.Text = _vm.AboutEntrySummary;
        }

        _appearanceGroup?.SetItems(
        [
            new ExpressiveGroupButtonItem("follow", "跟随系统", _vm.IsAppearanceFollowSystem),
            new ExpressiveGroupButtonItem("light", "浅色", _vm.IsAppearanceLight),
            new ExpressiveGroupButtonItem("dark", "深色", _vm.IsAppearanceDark),
        ]);
        _accentGroup?.SetItems(
        [
            new ExpressiveGroupButtonItem("follow", "跟随系统", _vm.IsAccentFollowSystem),
            new ExpressiveGroupButtonItem("blue", "品牌蓝", _vm.IsAccentBrandBlue),
            new ExpressiveGroupButtonItem("teal", "森林青", _vm.IsAccentForestTeal),
            new ExpressiveGroupButtonItem("sunset", "日落陶红", _vm.IsAccentSunsetTerracotta),
            new ExpressiveGroupButtonItem("violet", "紫罗兰", _vm.IsAccentVioletIndigo),
        ]);

        if (_vm.IsDaemonConnected)
        {
            _serviceActionGroup?.SetItems(
            [
                new ExpressiveGroupButtonItem("autostart", _vm.AutostartActionLabel),
            ]);
        }
        else
        {
            _serviceActionGroup?.SetItems(
            [
                new ExpressiveGroupButtonItem("autostart", _vm.AutostartActionLabel),
                new ExpressiveGroupButtonItem(
                    "start",
                    _vm.IsStartingDaemon ? "正在启动…" : "启动后台服务",
                    IsSelected: true),
            ]);
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
    }
}
