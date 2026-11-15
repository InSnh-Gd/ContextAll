using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ContextAll.App.Controls;

/// <summary>Color tone roles for <see cref="ExpressiveIconBadge"/>.</summary>
public enum BadgeTone
{
    Primary,
    Secondary,
    Tertiary,
    Error,
    Neutral,
}

/// <summary>
/// Vector icon seated inside an MD3E tonal pill badge.
/// </summary>
[PseudoClasses(":tone-primary", ":tone-secondary", ":tone-tertiary", ":tone-error", ":tone-neutral")]
[TemplatePart("PART_Icon", typeof(PathIcon))]
public class ExpressiveIconBadge : TemplatedControl
{
    public static readonly StyledProperty<string> PathDataProperty =
        AvaloniaProperty.Register<ExpressiveIconBadge, string>(nameof(PathData), string.Empty);

    public static readonly StyledProperty<BadgeTone> ToneProperty =
        AvaloniaProperty.Register<ExpressiveIconBadge, BadgeTone>(nameof(Tone), BadgeTone.Primary);

    public static readonly StyledProperty<double> ContainerSizeProperty =
        AvaloniaProperty.Register<ExpressiveIconBadge, double>(nameof(ContainerSize), 44.0);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ExpressiveIconBadge, double>(nameof(IconSize), 22.0);

    public static readonly StyledProperty<Geometry?> IconGeometryProperty =
        AvaloniaProperty.Register<ExpressiveIconBadge, Geometry?>(nameof(IconGeometry));

    public ExpressiveIconBadge()
    {
        UpdateTonePseudoClasses();
    }

    public string PathData
    {
        get => GetValue(PathDataProperty);
        set => SetValue(PathDataProperty, value);
    }

    public BadgeTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public double ContainerSize
    {
        get => GetValue(ContainerSizeProperty);
        set => SetValue(ContainerSizeProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public Geometry? IconGeometry
    {
        get => GetValue(IconGeometryProperty);
        set => SetValue(IconGeometryProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncGeometry();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncGeometry();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ToneProperty)
        {
            UpdateTonePseudoClasses();
        }
        else if (change.Property == PathDataProperty)
        {
            IconGeometry = Md3eResourceLookup.TryParseGeometry(PathData);
        }
    }

    private void SyncGeometry()
    {
        if (IconGeometry is null && !string.IsNullOrWhiteSpace(PathData))
        {
            IconGeometry = Md3eResourceLookup.TryParseGeometry(PathData);
        }
    }

    private void UpdateTonePseudoClasses()
    {
        PseudoClasses.Set(":tone-primary", Tone == BadgeTone.Primary);
        PseudoClasses.Set(":tone-secondary", Tone == BadgeTone.Secondary);
        PseudoClasses.Set(":tone-tertiary", Tone == BadgeTone.Tertiary);
        PseudoClasses.Set(":tone-error", Tone == BadgeTone.Error);
        PseudoClasses.Set(":tone-neutral", Tone == BadgeTone.Neutral);
    }
}
