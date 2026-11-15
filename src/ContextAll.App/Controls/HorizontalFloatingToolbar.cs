using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace ContextAll.App.Controls;

/// <summary>
/// MD3E compact bottom floating toolbar replacing fixed navigation bars on narrow windows (&lt; 840dp).
/// </summary>
[PseudoClasses(":devices", ":connection", ":transfers", ":thisdevice")]
[TemplatePart("PART_NavDevices", typeof(Button))]
[TemplatePart("PART_NavConnection", typeof(Button))]
[TemplatePart("PART_NavTransfers", typeof(Button))]
[TemplatePart("PART_NavThisDevice", typeof(Button))]
public class HorizontalFloatingToolbar : TemplatedControl
{
    public static readonly StyledProperty<ShellSection> SelectedSectionProperty =
        AvaloniaProperty.Register<HorizontalFloatingToolbar, ShellSection>(
            nameof(SelectedSection),
            ShellSection.Devices);

    private Button? _navDevices;
    private Button? _navConnection;
    private Button? _navTransfers;
    private Button? _navThisDevice;

    public HorizontalFloatingToolbar()
    {
        UpdatePseudoClasses();
    }

    public event Action<ShellSection>? SectionSelected;

    public ShellSection SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    /// <summary>Activates a section as if triggered by pointer/keyboard and raises <see cref="SectionSelected"/>.</summary>
    public void TriggerSection(ShellSection section)
    {
        SelectedSection = section;
        SectionSelected?.Invoke(section);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

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

        _navDevices = e.NameScope.Find<Button>("PART_NavDevices");
        _navConnection = e.NameScope.Find<Button>("PART_NavConnection");
        _navTransfers = e.NameScope.Find<Button>("PART_NavTransfers");
        _navThisDevice = e.NameScope.Find<Button>("PART_NavThisDevice");

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
        if (change.Property == SelectedSectionProperty)
        {
            UpdatePseudoClasses();
        }
    }

    private void OnDevicesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.Devices);

    private void OnConnectionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.Connection);

    private void OnTransfersClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.Transfers);

    private void OnThisDeviceClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerSection(ShellSection.ThisDevice);

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":devices", SelectedSection == ShellSection.Devices);
        PseudoClasses.Set(":connection", SelectedSection == ShellSection.Connection);
        PseudoClasses.Set(":transfers", SelectedSection == ShellSection.Transfers);
        PseudoClasses.Set(":thisdevice", SelectedSection == ShellSection.ThisDevice);
    }
}
