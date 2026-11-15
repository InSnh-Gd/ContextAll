using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace ContextAll.App.Controls;

/// <summary>
/// MD3E wide collapsible navigation rail (<c>96dp ↔ 224dp</c>) with synced Extended FAB and daemon pill.
/// </summary>
[PseudoClasses(":expanded", ":daemon-connected", ":devices", ":connection", ":transfers", ":thisdevice")]
[TemplatePart("PART_ToggleButton", typeof(Button))]
[TemplatePart("PART_FabButton", typeof(Button))]
[TemplatePart("PART_NavDevices", typeof(Button))]
[TemplatePart("PART_NavConnection", typeof(Button))]
[TemplatePart("PART_NavTransfers", typeof(Button))]
[TemplatePart("PART_NavThisDevice", typeof(Button))]
public class WideNavigationRail : TemplatedControl
{
    public const double CompactWidth = 96.0;
    public const double ExpandedWidth = 224.0;

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<WideNavigationRail, bool>(nameof(IsExpanded), false);

    public static readonly StyledProperty<ShellSection> SelectedSectionProperty =
        AvaloniaProperty.Register<WideNavigationRail, ShellSection>(nameof(SelectedSection), ShellSection.Devices);

    public static readonly StyledProperty<string> DaemonStatusLabelProperty =
        AvaloniaProperty.Register<WideNavigationRail, string>(nameof(DaemonStatusLabel), "离线");

    public static readonly StyledProperty<bool> IsDaemonConnectedProperty =
        AvaloniaProperty.Register<WideNavigationRail, bool>(nameof(IsDaemonConnected), false);

    public static readonly StyledProperty<string> FabTextProperty =
        AvaloniaProperty.Register<WideNavigationRail, string>(nameof(FabText), "发送文件");

    private Button? _toggleButton;
    private Button? _fabButton;
    private Button? _navDevices;
    private Button? _navConnection;
    private Button? _navTransfers;
    private Button? _navThisDevice;

    public WideNavigationRail()
    {
        UpdateState();
    }

    public event Action<ShellSection>? SectionSelected;

    public event Action? FabClicked;

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public ShellSection SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    public string DaemonStatusLabel
    {
        get => GetValue(DaemonStatusLabelProperty);
        set => SetValue(DaemonStatusLabelProperty, value);
    }

    public bool IsDaemonConnected
    {
        get => GetValue(IsDaemonConnectedProperty);
        set => SetValue(IsDaemonConnectedProperty, value);
    }

    public string FabText
    {
        get => GetValue(FabTextProperty);
        set => SetValue(FabTextProperty, value);
    }

    /// <summary>Activates a destination as if clicked by the user and raises <see cref="SectionSelected"/>.</summary>
    public void TriggerSection(ShellSection section)
    {
        SelectedSection = section;
        SectionSelected?.Invoke(section);
    }

    /// <summary>Activates the rail Extended FAB and raises <see cref="FabClicked"/>.</summary>
    public void TriggerFab() => FabClicked?.Invoke();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_toggleButton is not null)
        {
            _toggleButton.Click -= OnToggleClicked;
        }
        if (_fabButton is not null)
        {
            _fabButton.Click -= OnFabClicked;
        }
        if (_navDevices is not null)
        {
            _navDevices.Click -= OnDevicesClicked;
        }
        if (_navConnection is not null)
        {
            _navConnection.Click -= OnConnectionClicked;
        }
        if (_navTransfers is not null)
        {
            _navTransfers.Click -= OnTransfersClicked;
        }
        if (_navThisDevice is not null)
        {
            _navThisDevice.Click -= OnThisDeviceClicked;
        }

        _toggleButton = e.NameScope.Find<Button>("PART_ToggleButton");
        _fabButton = e.NameScope.Find<Button>("PART_FabButton");
        _navDevices = e.NameScope.Find<Button>("PART_NavDevices");
        _navConnection = e.NameScope.Find<Button>("PART_NavConnection");
        _navTransfers = e.NameScope.Find<Button>("PART_NavTransfers");
        _navThisDevice = e.NameScope.Find<Button>("PART_NavThisDevice");

        if (_toggleButton is not null)
        {
            _toggleButton.Click += OnToggleClicked;
        }
        if (_fabButton is not null)
        {
            _fabButton.Click += OnFabClicked;
        }
        if (_navDevices is not null)
        {
            _navDevices.Click += OnDevicesClicked;
        }
        if (_navConnection is not null)
        {
            _navConnection.Click += OnConnectionClicked;
        }
        if (_navTransfers is not null)
        {
            _navTransfers.Click += OnTransfersClicked;
        }
        if (_navThisDevice is not null)
        {
            _navThisDevice.Click += OnThisDeviceClicked;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty ||
            change.Property == SelectedSectionProperty ||
            change.Property == IsDaemonConnectedProperty)
        {
            UpdateState();
        }
    }

    private void OnToggleClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        IsExpanded = !IsExpanded;

    private void OnFabClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerFab();

    private void OnDevicesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.Devices);

    private void OnConnectionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.Connection);

    private void OnTransfersClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.Transfers);

    private void OnThisDeviceClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.ThisDevice);

    private void UpdateState()
    {
        Width = IsExpanded ? ExpandedWidth : CompactWidth;
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":daemon-connected", IsDaemonConnected);
        PseudoClasses.Set(":devices", SelectedSection == ShellSection.Devices);
        PseudoClasses.Set(":connection", SelectedSection == ShellSection.Connection);
        PseudoClasses.Set(":transfers", SelectedSection == ShellSection.Transfers);
        PseudoClasses.Set(":thisdevice", SelectedSection == ShellSection.ThisDevice);
    }
}
