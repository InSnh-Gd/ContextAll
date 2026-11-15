using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace ContextAll.App.Controls;

/// <summary>
/// MD3E switch featuring animated thumb scaling and crisp check/cross glyph states.
/// </summary>
[PseudoClasses(":checked")]
public class ExpressiveSwitch : TemplatedControl
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ExpressiveSwitch, bool>(
            nameof(IsChecked),
            false,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public ExpressiveSwitch()
    {
        Focusable = true;
        UpdatePseudoClasses();
    }

    public event Action<bool>? Toggled;

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Glyph rendered inside the switch thumb ("✓" when checked, "✕" otherwise).</summary>
    public string ThumbGlyph => IsChecked ? "✓" : "✕";

    /// <summary>Toggles <see cref="IsChecked"/> and raises <see cref="Toggled"/>.</summary>
    public void Toggle()
    {
        IsChecked = !IsChecked;
        Toggled?.Invoke(IsChecked);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsEnabled)
        {
            Toggle();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsEnabled && e.Key is Key.Space or Key.Enter)
        {
            Toggle();
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty)
        {
            UpdatePseudoClasses();
            RaisePropertyChanged(
                ThumbGlyphProperty,
                change.OldValue is bool oldVal ? (oldVal ? "✓" : "✕") : "✕",
                ThumbGlyph);
        }
    }

    public static readonly DirectProperty<ExpressiveSwitch, string> ThumbGlyphProperty =
        AvaloniaProperty.RegisterDirect<ExpressiveSwitch, string>(
            nameof(ThumbGlyph),
            o => o.ThumbGlyph);

    private void UpdatePseudoClasses() =>
        PseudoClasses.Set(":checked", IsChecked);
}
