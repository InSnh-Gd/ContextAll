using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ContextAll.App.Motion;

namespace ContextAll.App.Controls;

public readonly record struct RippleSnapshot(Point Center, double Radius, double Opacity);

/// <summary>FerroUI-aligned bounded/unbounded Material ripple overlay (75ms fade-in, 225ms expand, 150ms fade-out).</summary>
public sealed class MaterialRipple : Control
{
    public const double FadeInDurationSeconds = 0.075;
    public const double ExpandDurationSeconds = 0.225;
    public const double FadeOutDurationSeconds = 0.150;
    public const double PeakOpacity = 0.12;

    public static readonly StyledProperty<IBrush?> RippleBrushProperty =
        AvaloniaProperty.Register<MaterialRipple, IBrush?>(nameof(RippleBrush));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<MaterialRipple, CornerRadius>(nameof(CornerRadius), new CornerRadius(0));

    public static readonly StyledProperty<bool> IsBoundedProperty =
        AvaloniaProperty.Register<MaterialRipple, bool>(nameof(IsBounded), true);

    public static readonly StyledProperty<bool> ReducedMotionProperty =
        AvaloniaProperty.Register<MaterialRipple, bool>(nameof(ReducedMotion), false);

    private readonly List<RippleState> _ripples = [];
    private readonly MotionFrameDriver _driver;

    public MaterialRipple()
    {
        IsHitTestVisible = false;
        _driver = new MotionFrameDriver(this, dt =>
        {
            var active = StepInternal(dt);
            InvalidateVisual();
            return active;
        });
    }

    public IBrush? RippleBrush
    {
        get => GetValue(RippleBrushProperty);
        set => SetValue(RippleBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsBounded
    {
        get => GetValue(IsBoundedProperty);
        set => SetValue(IsBoundedProperty, value);
    }

    public bool ReducedMotion
    {
        get => GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    /// <summary>Optional test overrides when control has not been measured in a live layout pass.</summary>
    public double? BoundsWidthOverride { get; set; }
    public double? BoundsHeightOverride { get; set; }

    public bool HasActiveRipples => _ripples.Count > 0;

    public void AttachToHost(Control host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.PointerPressed += (_, e) =>
        {
            if (host.IsEffectivelyEnabled)
            {
                TriggerAt(e.GetPosition(host));
            }
        };
        host.PointerReleased += (_, _) => ReleaseActive();
        host.PointerCaptureLost += (_, _) => ReleaseActive();
        host.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod == NavigationMethod.Tab && host.IsEffectivelyEnabled)
            {
                TriggerCenter();
            }
        };
        host.LostFocus += (_, _) => ReleaseActive();
    }

    public void TriggerCenter()
    {
        var w = EffectiveWidth;
        var h = EffectiveHeight;
        TriggerAt(new Point(w * 0.5, h * 0.5));
    }

    public void TriggerAt(Point origin)
    {
        var w = EffectiveWidth;
        var h = EffectiveHeight;
        var targetRadius = ComputeMaxRadius(origin, w, h);

        foreach (var existing in _ripples)
        {
            existing.Released = true;
        }

        _ripples.Add(new RippleState(origin, targetRadius, ReducedMotion));
        _driver.Start();
        InvalidateVisual();
    }

    public void ReleaseActive()
    {
        foreach (var ripple in _ripples)
        {
            ripple.Released = true;
        }

        if (_ripples.Count > 0)
        {
            _driver.Start();
        }
    }

    public void AdvanceTime(double deltaSeconds)
    {
        StepInternal(deltaSeconds);
    }

    public IReadOnlyList<RippleSnapshot> SnapshotRipples() =>
        [.. _ripples.Select(r => new RippleSnapshot(r.Origin, r.CurrentRadius, r.CurrentOpacity))];

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_ripples.Count == 0)
        {
            return;
        }

        var w = EffectiveWidth;
        var h = EffectiveHeight;
        var baseColor = ResolveRippleColor();

        IDisposable? clipScope = null;
        if (IsBounded && w > 0 && h > 0)
        {
            var r = CornerRadius.TopLeft;
            clipScope = context.PushClip(new RoundedRect(new Rect(0, 0, w, h), r, r));
        }

        try
        {
            foreach (var ripple in _ripples)
            {
                if (ripple.CurrentOpacity <= 0.001 || ripple.CurrentRadius <= 0.1)
                {
                    continue;
                }

                var alpha = (byte)Math.Clamp(Math.Round(ripple.CurrentOpacity * 255.0), 0, 255);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                context.DrawEllipse(brush, pen: null, ripple.Origin, ripple.CurrentRadius, ripple.CurrentRadius);
            }
        }
        finally
        {
            clipScope?.Dispose();
        }
    }

    private double EffectiveWidth => BoundsWidthOverride ?? Math.Max(0.0, Bounds.Width);
    private double EffectiveHeight => BoundsHeightOverride ?? Math.Max(0.0, Bounds.Height);

    private bool StepInternal(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return _ripples.Count > 0;
        }

        for (var i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            ripple.Advance(deltaSeconds);
            if (ripple.IsFinished)
            {
                _ripples.RemoveAt(i);
            }
        }

        return _ripples.Count > 0;
    }

    private Color ResolveRippleColor()
    {
        if (RippleBrush is ISolidColorBrush solid)
        {
            return solid.Color;
        }

        if (this.TryFindResource("OnSurfaceColor", ActualThemeVariant, out var res) && res is Color c)
        {
            return c;
        }

        return Colors.Gray;
    }

    private static double ComputeMaxRadius(Point origin, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return 24.0;
        }

        var d1 = double.Hypot(origin.X, origin.Y);
        var d2 = double.Hypot(width - origin.X, origin.Y);
        var d3 = double.Hypot(origin.X, height - origin.Y);
        var d4 = double.Hypot(width - origin.X, height - origin.Y);
        return Math.Max(Math.Max(d1, d2), Math.Max(d3, d4)) + 4.0;
    }

    private sealed class RippleState(Point origin, double targetRadius, bool reducedMotion)
    {
        private double _elapsed;
        private double _fadeElapsed;

        public Point Origin { get; } = origin;
        public double TargetRadius { get; } = targetRadius;
        public bool Released { get; set; }
        public double CurrentRadius { get; private set; } = reducedMotion ? targetRadius : targetRadius * 0.12;
        public double CurrentOpacity { get; private set; } = reducedMotion ? PeakOpacity : 0.0;
        public bool IsFinished { get; private set; }

        public void Advance(double dt)
        {
            _elapsed += dt;

            if (reducedMotion)
            {
                CurrentRadius = TargetRadius;
                if (Released)
                {
                    _fadeElapsed += dt;
                    var fadeT = Math.Clamp(_fadeElapsed / FadeOutDurationSeconds, 0.0, 1.0);
                    CurrentOpacity = PeakOpacity * (1.0 - fadeT);
                    if (fadeT >= 1.0)
                    {
                        IsFinished = true;
                    }
                }
                else
                {
                    CurrentOpacity = PeakOpacity;
                }

                return;
            }

            var expandT = Math.Clamp(_elapsed / ExpandDurationSeconds, 0.0, 1.0);
            var easedExpand = 1.0 - Math.Pow(1.0 - expandT, 3.0);
            CurrentRadius = TargetRadius * (0.12 + 0.88 * easedExpand);

            var fadeInT = Math.Clamp(_elapsed / FadeInDurationSeconds, 0.0, 1.0);
            if (!Released || _elapsed < ExpandDurationSeconds)
            {
                CurrentOpacity = PeakOpacity * fadeInT;
                return;
            }

            _fadeElapsed += dt;
            var fadeOutT = Math.Clamp(_fadeElapsed / FadeOutDurationSeconds, 0.0, 1.0);
            CurrentOpacity = PeakOpacity * (1.0 - fadeOutT);
            if (fadeOutT >= 1.0)
            {
                IsFinished = true;
            }
        }
    }
}
