using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ContextAll.App.Chrome;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// Root MD3E compiled AXAML shell surface hosting adaptive CSD chrome,
/// <see cref="WideNavigationRail"/> / <see cref="HorizontalFloatingToolbar"/>, four sections,
/// onboarding banner, corrupt-state modal dialog, and <see cref="MaterialSnackbarHost"/>.
/// </summary>
public partial class ShellRootView : UserControl
{
    private readonly ShellViewModel _vm;
    private readonly Border _chromeBorder;
    private readonly IntegratedHeaderBar _headerBar;
    private readonly WideNavigationRail _rail;
    private readonly HorizontalFloatingToolbar _dock;
    private readonly Border _onboardingCard;
    private readonly DevicesView _devicesView;
    private readonly ConnectionView _connectionView;
    private readonly TransfersView _transfersView;
    private readonly ThisDeviceView _thisDeviceView;
    private readonly MaterialDialog _corruptDialog;
    private readonly MaterialSnackbarHost _snackbarHost;

    public ShellRootView()
        : this(new ShellViewModel(
            IdleLocalChannel.Shared,
            new Platform.LinuxDaemonController(),
            new Platform.ShellPreferences(),
            _ => { }))
    {
    }

    public ShellRootView(ShellViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        AvaloniaXamlLoader.Load(this);

        _chromeBorder = this.FindControl<Border>("ChromeBorder") ?? new Border();
        _headerBar = this.FindControl<IntegratedHeaderBar>("HeaderBarControl") ?? new IntegratedHeaderBar();
        _rail = this.FindControl<WideNavigationRail>("NavRailControl") ?? new WideNavigationRail();
        _dock = this.FindControl<HorizontalFloatingToolbar>("FloatingToolbarControl") ?? new HorizontalFloatingToolbar();
        _onboardingCard = this.FindControl<Border>("OnboardingCard") ?? new Border();
        _corruptDialog = this.FindControl<MaterialDialog>("CorruptDialogControl") ?? new MaterialDialog();
        _snackbarHost = this.FindControl<MaterialSnackbarHost>("SnackbarHostControl") ?? new MaterialSnackbarHost();

        var devicesSlot = this.FindControl<ContentControl>("DevicesSlot");
        var connectionSlot = this.FindControl<ContentControl>("ConnectionSlot");
        var transfersSlot = this.FindControl<ContentControl>("TransfersSlot");
        var thisDeviceSlot = this.FindControl<ContentControl>("ThisDeviceSlot");

        _devicesView = new DevicesView(_vm.Devices, _vm.NavigateToSendFilesForPeer);
        _connectionView = new ConnectionView(_vm.Connection);
        _transfersView = new TransfersView(_vm.Transfers);
        _thisDeviceView = new ThisDeviceView(_vm.ThisDevice);

        _devicesView.NavigateToConnectionRequested += () => _vm.SelectedSection = ShellSection.Connection;
        _transfersView.NavigateToDevicesRequested += () => _vm.SelectedSection = ShellSection.Devices;
        _thisDeviceView.OpenGuideRequested += () =>
        {
            _vm.SelectedSection = ShellSection.Connection;
            _vm.Connection.OpenGuide();
        };

        if (devicesSlot is not null)
        {
            devicesSlot.Content = _devicesView;
        }
        if (connectionSlot is not null)
        {
            connectionSlot.Content = _connectionView;
        }
        if (transfersSlot is not null)
        {
            transfersSlot.Content = _transfersView;
        }
        if (thisDeviceSlot is not null)
        {
            thisDeviceSlot.Content = _thisDeviceView;
        }

        _rail.SectionSelected += s => _vm.SelectedSection = s;
        _rail.FabClicked += () => _vm.TriggerRailFab();
        _dock.SectionSelected += s => _vm.SelectedSection = s;

        _corruptDialog.PrimaryActionClicked += async (_, _) => await _vm.ConfirmResetAndStartAsync();
        _corruptDialog.Canceled += (_, _) => _vm.CancelCorruptReset();

        _vm.SnackbarRequested += (message, actionText, callback) =>
            _snackbarHost.Enqueue(message, actionText, callback);

        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public IntegratedHeaderBar HeaderBar => _headerBar;
    public DevicesView DevicesView => _devicesView;
    public ThisDeviceView ThisDeviceView => _thisDeviceView;
    public MaterialDialog CorruptDialog => _corruptDialog;
    public MaterialSnackbarHost SnackbarHost => _snackbarHost;
    public bool IsRailVisible => _rail.IsVisible;
    public bool IsDockVisible => _dock.IsVisible;
    public bool IsOnboardingVisible => _onboardingCard.IsVisible;
    public bool IsCorruptModalVisible => _corruptDialog.IsOpen;

    public Thickness ChromeBorderThickness => _chromeBorder.BorderThickness;
    public bool IsHeaderVisible => _headerBar.IsVisible;

    public void ApplyChrome(ChromeMode chrome)
    {
        _headerBar.IsVisible = chrome.ShowIntegratedHeader;
        _headerBar.IsWindowMaximized = chrome.OuterCornerRadius.TopLeft <= 0.0;
        _chromeBorder.CornerRadius = chrome.OuterCornerRadius;
        _chromeBorder.BorderThickness = chrome.ChromeBorderThickness;
        _thisDeviceView.ApplyTitleBarStatus(chrome.TitleBarStatus);
    }

    public void SetHeaderButtonLayout(WindowButtonLayout layout) =>
        _headerBar.ButtonLayout = layout;

    private async void OnStartDaemonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.StartDaemonExplicitAsync();

    private async void OnEnableAutostartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.EnableAutostartExplicitAsync();

    private void Refresh()
    {
        var wide = _vm.IsWideLayout;
        _connectionView.ApplyResponsiveLayout(wide);
        _thisDeviceView.ApplyResponsiveLayout(wide);
        _rail.IsVisible = wide;
        _dock.IsVisible = !wide;
        _rail.SelectedSection = _vm.SelectedSection;
        _dock.SelectedSection = _vm.SelectedSection;

        _headerBar.SubtitleText = _vm.Connected && !string.IsNullOrWhiteSpace(_vm.LocalDisplayName)
            ? _vm.LocalDisplayName
            : _vm.Connected ? "已连接" : "未连接";

        _corruptDialog.IsOpen = _vm.IsCorruptStateDialogOpen;
        _corruptDialog.Message = string.IsNullOrWhiteSpace(_vm.CorruptStateError)
            ? "检测到本机身份或配置文件已损坏。点击取消将保留现有文件；确认后将重置本机身份并重启后台服务。"
            : _vm.CorruptStateError;
    }
}
