using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextAll.App.Motion;

namespace ContextAll.App.Controls;

/// <summary>Snapshot of the computed wavy progress bar geometry for a given size and phase.</summary>
public sealed record WavyFrameGeometry(
    Geometry? ActiveWave,
    Geometry? RemainingTrack,
    Point? StopIndicatorCenter,
    double StopIndicatorRadius,
    double GapWidth,
    bool HasStopIndicator);

/// <summary>
/// MD3E linear wavy progress indicator with sine-wave active segment, 4dp gap, and trailing stop dot.
/// </summary>
public class LinearWavyProgressIndicator : TemplatedControl
{
    public const double DefaultGapWidth = 4.0;
    public const double DefaultAmplitude = 3.0;
    public const double DefaultWavelength = 22.0;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LinearWavyProgressIndicator, double>(nameof(Value), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LinearWavyProgressIndicator, double>(nameof(Maximum), 100.0);

    public static readonly StyledProperty<bool> ReducedMotionProperty =
        AvaloniaProperty.Register<LinearWavyProgressIndicator, bool>(nameof(ReducedMotion), false);

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<LinearWavyProgressIndicator, IBrush?>(nameof(IndicatorBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<LinearWavyProgressIndicator, IBrush?>(nameof(TrackBrush));

    private MotionFrameDriver? _driver;
    private double _phaseRadians;

    static LinearWavyProgressIndicator()
    {
        AffectsRender<LinearWavyProgressIndicator>(
            ValueProperty,
            MaximumProperty,
            ReducedMotionProperty,
            IndicatorBrushProperty,
            TrackBrushProperty);
    }

    public LinearWavyProgressIndicator()
    {
        MinHeight = 16;
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public bool ReducedMotion
    {
        get => GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Normalized completion ratio in <c>[0.0, 1.0]</c>.</summary>
    public double Fraction => Maximum <= 0.0
        ? 0.0
        : Math.Clamp(Value / Maximum, 0.0, 1.0);

    /// <summary>Whether the active segment and inactive track are separated by the 4dp M3E gap.</summary>
    public bool ShowGap => Fraction > 0.0 && Fraction < 1.0;

    /// <summary>Computes the active sine-wave path, remaining rounded track, and stop indicator center.</summary>
    public WavyFrameGeometry BuildFrameGeometry(double width, double height, double phaseRadians = 0.0)
    {
        var layout = WavyProgressGeometry.Compute(
            width,
            height,
            Fraction,
            phaseRadians,
            strokeThickness: 4.0);

        Geometry? activeWave = null;
        if (layout.ActiveWavePoints.Count >= 2)
        {
            var stream = new StreamGeometry();
            using (var ctx = stream.Open())
            {
                ctx.BeginFigure(layout.ActiveWavePoints[0], isFilled: false);
                for (var i = 1; i < layout.ActiveWavePoints.Count; i++)
                {
                    ctx.LineTo(layout.ActiveWavePoints[i]);
                }

                ctx.EndFigure(isClosed: false);
            }

            activeWave = stream;
        }

        Geometry? remainingTrack = null;
        var centerY = layout.StopDotCenter.Y;
        if (layout.InactiveTrackEndX - layout.InactiveTrackStartX >= 2.0)
        {
            var stream = new StreamGeometry();
            using (var ctx = stream.Open())
            {
                ctx.BeginFigure(new Point(layout.InactiveTrackStartX, centerY), isFilled: false);
                ctx.LineTo(new Point(layout.InactiveTrackEndX, centerY));
                ctx.EndFigure(isClosed: false);
            }

            remainingTrack = stream;
        }

        return new WavyFrameGeometry(
            ActiveWave: activeWave,
            RemainingTrack: remainingTrack,
            StopIndicatorCenter: layout.HasGap || Fraction < 1.0 ? layout.StopDotCenter : null,
            StopIndicatorRadius: layout.StopDotRadius,
            GapWidth: DefaultGapWidth,
            HasStopIndicator: layout.HasGap || Fraction < 1.0);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var frame = BuildFrameGeometry(Bounds.Width, Bounds.Height, ReducedMotion ? 0.0 : _phaseRadians);

        if (frame.RemainingTrack is not null && TrackBrush is not null)
        {
            var trackPen = new Pen(TrackBrush, 4.0, lineCap: PenLineCap.Round);
            context.DrawGeometry(null, trackPen, frame.RemainingTrack);
        }

        if (frame.ActiveWave is not null && IndicatorBrush is not null)
        {
            var wavePen = new Pen(IndicatorBrush, 4.0, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, wavePen, frame.ActiveWave);
        }

        if (frame.HasStopIndicator && frame.StopIndicatorCenter is { } stopCenter && IndicatorBrush is not null)
        {
            context.DrawEllipse(IndicatorBrush, null, stopCenter, frame.StopIndicatorRadius, frame.StopIndicatorRadius);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureAnimationLoop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _driver?.Stop();
        _driver = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty ||
            change.Property == MaximumProperty ||
            change.Property == ReducedMotionProperty)
        {
            UpdateAnimationState();
        }
    }

    private void EnsureAnimationLoop()
    {
        _driver ??= new MotionFrameDriver(this, dt =>
        {
            _phaseRadians = (_phaseRadians + dt * 2.4) % (Math.PI * 2.0);
            InvalidateVisual();
            return ShowGap && !ReducedMotion;
        });
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (_driver is null)
        {
            return;
        }

        if (ShowGap && !ReducedMotion)
        {
            _driver.Start();
        }
        else
        {
            _driver.Stop();
            InvalidateVisual();
        }
    }
}
