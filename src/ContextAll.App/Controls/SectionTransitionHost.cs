using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ContextAll.App.Motion;

namespace ContextAll.App.Controls;

/// <summary>
/// Hosts the four shell sections and plays an MD3E Expressive shared-axis X transition
/// (DefaultSpatial slide + Default/FastEffects opacity) when <see cref="SelectedSection"/> changes.
/// Moving layers are bitmap-cached so each frame composites two snapshots instead of relayouting
/// two full MD3E trees.
/// </summary>
public sealed class SectionTransitionHost : Panel
{
    public static readonly AttachedProperty<ShellSection> SectionProperty =
        AvaloniaProperty.RegisterAttached<SectionTransitionHost, Control, ShellSection>("Section");

    public static readonly StyledProperty<ShellSection> SelectedSectionProperty =
        AvaloniaProperty.Register<SectionTransitionHost, ShellSection>(nameof(SelectedSection), ShellSection.Devices);

    public static readonly StyledProperty<bool> ReducedMotionProperty =
        AvaloniaProperty.Register<SectionTransitionHost, bool>(nameof(ReducedMotion), false);

    private readonly SpringSolver _spatial = new(MotionScheme.Expressive(MotionRole.DefaultSpatial));
    private readonly SpringSolver _incomingOpacity = new(MotionScheme.Expressive(MotionRole.DefaultEffects));
    private readonly SpringSolver _outgoingOpacity = new(MotionScheme.Expressive(MotionRole.FastEffects), initialValue: 1.0);
    private readonly TranslateTransform _incomingTransform = new();
    private readonly TranslateTransform _outgoingTransform = new();
    private readonly MotionFrameDriver _driver;

    private Control? _incoming;
    private Control? _outgoing;
    private int _direction;
    private bool _transitioning;
    private bool _warmupFrame;

    static SectionTransitionHost()
    {
        SectionProperty.Changed.AddClassHandler<Control>(OnAttachedSectionChanged);
    }

    public SectionTransitionHost()
    {
        ClipToBounds = true;
        UseLayoutRounding = false;
        _driver = new MotionFrameDriver(this, StepInternal);
        Children.CollectionChanged += (_, _) =>
        {
            if (!_transitioning)
            {
                SnapTo(SelectedSection);
            }
        };
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (!_transitioning)
        {
            SnapTo(SelectedSection);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_transitioning)
        {
            SnapTo(SelectedSection);
        }
    }

    public ShellSection SelectedSection
    {
        get => GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    public bool ReducedMotion
    {
        get => GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    public bool IsTransitioning => _transitioning;

    public static void SetSection(AvaloniaObject element, ShellSection value) =>
        element.SetValue(SectionProperty, value);

    public static ShellSection GetSection(AvaloniaObject element) =>
        element.GetValue(SectionProperty);

    public SharedAxisFrame Snapshot() =>
        ExpressiveSharedAxis.Interpolate(_direction, _spatial.Value, _incomingOpacity.Value, _outgoingOpacity.Value);

    public void AdvanceTime(double deltaSeconds)
    {
        var remaining = Math.Max(0.0, deltaSeconds);
        while (remaining > 0.0 && _transitioning)
        {
            var step = Math.Min(remaining, 0.05);
            if (!StepInternal(step))
            {
                return;
            }

            remaining -= step;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedSectionProperty)
        {
            BeginTransition();
        }
        else if (change.Property == ReducedMotionProperty)
        {
            _spatial.ReducedMotion = ReducedMotion;
            _incomingOpacity.ReducedMotion = ReducedMotion;
            _outgoingOpacity.ReducedMotion = ReducedMotion;
            if (ReducedMotion && _transitioning)
            {
                SnapTo(SelectedSection);
            }
        }
    }

    private void BeginTransition()
    {
        var incoming = FindSection(SelectedSection);
        if (incoming is null)
        {
            return;
        }

        var outgoing = _incoming ?? FindVisibleSection();
        if (ReferenceEquals(outgoing, incoming) || ReducedMotion || outgoing is null)
        {
            SnapTo(SelectedSection);
            return;
        }

        _driver.Stop();
        HideExcept(outgoing, incoming);

        _outgoing = outgoing;
        _incoming = incoming;
        _direction = ExpressiveSharedAxis.Direction(GetSection(outgoing), SelectedSection);
        if (_direction == 0)
        {
            SnapTo(SelectedSection);
            return;
        }

        _spatial.ReducedMotion = ReducedMotion;
        _incomingOpacity.ReducedMotion = ReducedMotion;
        _outgoingOpacity.ReducedMotion = ReducedMotion;
        _spatial.SnapTo(0.0);
        _spatial.SetTarget(1.0);
        _incomingOpacity.SnapTo(0.0);
        _incomingOpacity.SetTarget(1.0);
        _outgoingOpacity.SnapTo(1.0);
        _outgoingOpacity.SetTarget(0.0);

        incoming.IsVisible = true;
        incoming.Opacity = 0.0;
        incoming.IsHitTestVisible = true;
        incoming.RenderTransform = _incomingTransform;
        incoming.ZIndex = 1;
        ArmLayer(incoming);

        outgoing.IsVisible = true;
        outgoing.IsHitTestVisible = false;
        outgoing.RenderTransform = _outgoingTransform;
        outgoing.ZIndex = 0;
        ArmLayer(outgoing);

        _transitioning = true;
        _warmupFrame = true;
        ApplyFrame();
        UpdateLayout();
        _driver.Start();
    }

    private bool StepInternal(double deltaSeconds)
    {
        if (!_transitioning)
        {
            return false;
        }

        if (_warmupFrame)
        {
            _warmupFrame = false;
            ApplyFrame();
            return true;
        }

        _spatial.Step(deltaSeconds);
        _incomingOpacity.Step(deltaSeconds);
        _outgoingOpacity.Step(deltaSeconds);
        ApplyFrame();

        if (_spatial.IsSettled && _incomingOpacity.IsSettled && _outgoingOpacity.IsSettled)
        {
            SnapTo(SelectedSection);
            return false;
        }

        return true;
    }

    private void ApplyFrame()
    {
        var frame = Snapshot();
        _incomingTransform.X = frame.IncomingOffset;
        _outgoingTransform.X = frame.OutgoingOffset;
        if (_incoming is not null)
        {
            _incoming.Opacity = frame.IncomingOpacity;
        }

        if (_outgoing is not null)
        {
            _outgoing.Opacity = frame.OutgoingOpacity;
        }
    }

    private void SnapTo(ShellSection section)
    {
        _driver.Stop();
        _transitioning = false;
        _warmupFrame = false;
        _incoming = FindSection(section);
        _outgoing = null;
        _incomingTransform.X = 0.0;
        _outgoingTransform.X = 0.0;
        foreach (var child in Children)
        {
            var selected = GetSection(child) == section;
            child.IsVisible = selected;
            child.Opacity = 1.0;
            child.RenderTransform = null;
            child.IsHitTestVisible = selected;
            child.ZIndex = selected ? 1 : 0;
            DisarmLayer(child, keepCache: selected);
        }
    }

    private void HideExcept(Control first, Control second)
    {
        foreach (var child in Children)
        {
            if (!ReferenceEquals(child, first) && !ReferenceEquals(child, second))
            {
                child.IsVisible = false;
                child.IsHitTestVisible = false;
                DisarmLayer(child, keepCache: false);
            }
        }
    }

    private static void OnAttachedSectionChanged(Control control, AvaloniaPropertyChangedEventArgs _)
    {
        if (control.Parent is SectionTransitionHost host && !host._transitioning)
        {
            host.SnapTo(host.SelectedSection);
        }
    }

    private Control? FindSection(ShellSection section)
    {
        foreach (var child in Children)
        {
            if (GetSection(child) == section)
            {
                return child;
            }
        }

        return null;
    }

    private Control? FindVisibleSection()
    {
        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                return child;
            }
        }

        return FindSection(SelectedSection);
    }

    private static Visual CacheTarget(Control slot) =>
        slot is ContentControl { Content: Visual inner } ? inner : slot;

    private static BitmapCache NewSectionCache() =>
        new()
        {
            RenderAtScale = 1.0,
            SnapsToDevicePixels = false,
        };

    private static void ArmLayer(Control slot)
    {
        slot.UseLayoutRounding = false;
        var target = CacheTarget(slot);
        target.CacheMode ??= NewSectionCache();
    }

    private static void DisarmLayer(Control slot, bool keepCache)
    {
        slot.UseLayoutRounding = true;
        var target = CacheTarget(slot);
        if (keepCache)
        {
            target.CacheMode ??= NewSectionCache();
            return;
        }

        target.CacheMode = null;
        if (!ReferenceEquals(target, slot))
        {
            slot.CacheMode = null;
        }
    }
}
