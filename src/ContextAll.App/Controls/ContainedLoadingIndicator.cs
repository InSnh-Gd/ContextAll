using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextAll.App.Motion;

namespace ContextAll.App.Controls;

/// <summary>
/// MD3E contained loading indicator morphing smoothly across 7 expressive normalized polygons.
/// </summary>
[PseudoClasses(":active")]
public class ContainedLoadingIndicator : TemplatedControl
{
    public const double FullCycleDurationSeconds = 8.4;

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ContainedLoadingIndicator, bool>(nameof(IsActive), true);

    public static readonly StyledProperty<bool> ReducedMotionProperty =
        AvaloniaProperty.Register<ContainedLoadingIndicator, bool>(nameof(ReducedMotion), false);

    public static readonly StyledProperty<IBrush?> ContainerBrushProperty =
        AvaloniaProperty.Register<ContainedLoadingIndicator, IBrush?>(nameof(ContainerBrush));

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<ContainedLoadingIndicator, IBrush?>(nameof(IndicatorBrush));

    private MotionFrameDriver? _driver;
    private double _elapsedSeconds;
    private double _morphProgress;

    static ContainedLoadingIndicator()
    {
        AffectsRender<ContainedLoadingIndicator>(
            IsActiveProperty,
            ReducedMotionProperty,
            ContainerBrushProperty,
            IndicatorBrushProperty);
    }

    public ContainedLoadingIndicator()
    {
        Width = 48;
        Height = 48;
        UpdatePseudoClasses();
    }

    public int PolygonCount => PolygonMorphGeometry.ShapeCount;

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool ReducedMotion
    {
        get => GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    public IBrush? ContainerBrush
    {
        get => GetValue(ContainerBrushProperty);
        set => SetValue(ContainerBrushProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    /// <summary>Index of the polygon currently leading the morph sequence.</summary>
    public int CurrentPolygonIndex => ReducedMotion
        ? 0
        : (int)Math.Floor(_morphProgress * PolygonCount) % PolygonCount;

    /// <summary>Builds a morphed <see cref="StreamGeometry"/> scaled into a square viewport of <paramref name="size"/>.</summary>
    public Geometry BuildMorphedGeometry(double size, double progress)
    {
        var count = PolygonCount;
        var scaled = Math.Clamp(progress, 0.0, 1.0) * count;
        var fromIndex = ((int)Math.Floor(scaled)) % count;
        var toIndex = (fromIndex + 1) % count;
        var localT = scaled - Math.Floor(scaled);
        var easedT = ReducedMotion ? 0.0 : localT * localT * (3.0 - 2.0 * localT);
        var rotation = ReducedMotion ? 0.0 : progress * Math.PI * 2.0;

        var vertices = PolygonMorphGeometry.Interpolate(
            fromIndex,
            toIndex,
            easedT,
            rotationRadians: rotation);

        var inset = size * 0.14;
        var span = Math.Max(4.0, size - inset * 2.0);
        var stream = new StreamGeometry();
        using (var ctx = stream.Open())
        {
            if (vertices.Count > 0)
            {
                ctx.BeginFigure(
                    new Point(inset + vertices[0].X * span, inset + vertices[0].Y * span),
                    isFilled: true);
                for (var i = 1; i < vertices.Count; i++)
                {
                    ctx.LineTo(new Point(inset + vertices[i].X * span, inset + vertices[i].Y * span));
                }

                ctx.EndFigure(isClosed: true);
            }
        }

        return stream;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 2)
        {
            return;
        }

        var radius = size / 2.0;
        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
        if (ContainerBrush is not null)
        {
            context.DrawEllipse(ContainerBrush, null, center, radius, radius);
        }

        if (IndicatorBrush is not null)
        {
            var shape = BuildMorphedGeometry(size, ReducedMotion ? 0.0 : _morphProgress);
            var offset = new Vector((Bounds.Width - size) / 2.0, (Bounds.Height - size) / 2.0);
            using (context.PushTransform(Matrix.CreateTranslation(offset)))
            {
                context.DrawGeometry(IndicatorBrush, null, shape);
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _driver ??= new MotionFrameDriver(this, dt =>
        {
            _elapsedSeconds += dt;
            _morphProgress = (_elapsedSeconds / FullCycleDurationSeconds) % 1.0;
            InvalidateVisual();
            return IsActive && !ReducedMotion;
        });
        UpdateAnimationLoop();
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
        if (change.Property == IsActiveProperty || change.Property == ReducedMotionProperty)
        {
            if (ReducedMotion)
            {
                _morphProgress = 0.0;
            }

            UpdatePseudoClasses();
            UpdateAnimationLoop();
            InvalidateVisual();
        }
    }

    private void UpdatePseudoClasses() =>
        PseudoClasses.Set(":active", IsActive);

    private void UpdateAnimationLoop()
    {
        if (_driver is null)
        {
            return;
        }

        if (IsActive && !ReducedMotion)
        {
            _driver.Start();
        }
        else
        {
            _driver.Stop();
        }
    }
}
