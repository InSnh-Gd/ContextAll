using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace ContextAll.App.Controls;

/// <summary>Position of an item within a segmented expressive group container.</summary>
public enum GroupPosition
{
    Single,
    First,
    Middle,
    Last,
}

/// <summary>
/// MD3E segmented container tile supporting positional corner radii, hover/press morphing, and ripple feedback.
/// </summary>
[PseudoClasses(":interactive", ":selected", ":hovered", ":pressed")]
[TemplatePart("PART_Ripple", typeof(MaterialRipple))]
public class ExpressiveGroupTile : ContentControl
{
    public const double OuterRadius = 28.0;
    public const double InnerRadius = 6.0;
    public const double InnerMorphRadius = 12.0;
    public const double SingleHoverRadius = 22.0;
    public const double PressedRadius = 12.0;

    public static readonly StyledProperty<GroupPosition> PositionProperty =
        AvaloniaProperty.Register<ExpressiveGroupTile, GroupPosition>(nameof(Position), GroupPosition.Single);

    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<ExpressiveGroupTile, bool>(nameof(IsInteractive), true);

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<ExpressiveGroupTile, bool>(nameof(IsSelected), false);

    public static readonly StyledProperty<bool> IsHoveredStateProperty =
        AvaloniaProperty.Register<ExpressiveGroupTile, bool>(nameof(IsHoveredState), false);

    public static readonly StyledProperty<bool> IsPressedStateProperty =
        AvaloniaProperty.Register<ExpressiveGroupTile, bool>(nameof(IsPressedState), false);

    private MaterialRipple? _ripple;

    public ExpressiveGroupTile()
    {
        Focusable = true;
        UpdateCornerRadiusAndPseudoClasses();
    }

    public event Action? Activated;

    public GroupPosition Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsHoveredState
    {
        get => GetValue(IsHoveredStateProperty);
        set => SetValue(IsHoveredStateProperty, value);
    }

    public bool IsPressedState
    {
        get => GetValue(IsPressedStateProperty);
        set => SetValue(IsPressedStateProperty, value);
    }

    /// <summary>Compatibility alias over <see cref="ContentControl.Content"/> for code-built views.</summary>
    public Control? Child
    {
        get => Content as Control;
        set => Content = value;
    }

    /// <summary>Computes the positional and interactive M3E corner radii for a grouped item.</summary>
    public static CornerRadius ComputeCornerRadius(
        GroupPosition position,
        bool isHovered,
        bool isPressed,
        bool isSelected,
        bool isInteractive = true)
    {
        if (isInteractive && isPressed)
        {
            return new CornerRadius(PressedRadius);
        }

        if (isSelected)
        {
            return new CornerRadius(OuterRadius);
        }

        var morph = isInteractive && isHovered;
        var inner = morph ? InnerMorphRadius : InnerRadius;

        return position switch
        {
            GroupPosition.First => new CornerRadius(OuterRadius, OuterRadius, inner, inner),
            GroupPosition.Middle => new CornerRadius(inner),
            GroupPosition.Last => new CornerRadius(inner, inner, OuterRadius, OuterRadius),
            _ => new CornerRadius(morph ? SingleHoverRadius : OuterRadius),
        };
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _ripple = e.NameScope.Find<MaterialRipple>("PART_Ripple");
        if (_ripple is not null)
        {
            _ripple.AttachToHost(this);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PositionProperty ||
            change.Property == IsInteractiveProperty ||
            change.Property == IsSelectedProperty ||
            change.Property == IsHoveredStateProperty ||
            change.Property == IsPressedStateProperty)
        {
            UpdateCornerRadiusAndPseudoClasses();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (IsInteractive)
        {
            IsHoveredState = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        IsHoveredState = false;
        IsPressedState = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsInteractive && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            IsPressedState = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasPressed = IsPressedState;
        IsPressedState = false;
        if (IsInteractive && wasPressed)
        {
            Activated?.Invoke();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsInteractive && e.Key is Key.Enter or Key.Space)
        {
            Activated?.Invoke();
            e.Handled = true;
        }
    }

    private void UpdateCornerRadiusAndPseudoClasses()
    {
        PseudoClasses.Set(":interactive", IsInteractive);
        PseudoClasses.Set(":selected", IsSelected);
        PseudoClasses.Set(":hovered", IsHoveredState);
        PseudoClasses.Set(":pressed", IsPressedState);
        CornerRadius = ComputeCornerRadius(Position, IsHoveredState, IsPressedState, IsSelected, IsInteractive);
    }
}
