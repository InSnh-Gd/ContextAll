using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace ContextAll.App.Controls;

/// <summary>
/// MD3E modal confirmation dialog with 28dp rounded surface, scrim dismissal, and default button focus policy.
/// </summary>
[PseudoClasses(":open")]
[TemplatePart("PART_Scrim", typeof(Border))]
[TemplatePart("PART_PrimaryButton", typeof(Button))]
[TemplatePart("PART_CancelButton", typeof(Button))]
public class MaterialDialog : TemplatedControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<MaterialDialog, bool>(nameof(IsOpen), false);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MaterialDialog, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<MaterialDialog, string>(nameof(Message), string.Empty);

    public static readonly StyledProperty<object?> BodyContentProperty =
        AvaloniaProperty.Register<MaterialDialog, object?>(nameof(BodyContent));

    public static readonly StyledProperty<string> PrimaryActionTextProperty =
        AvaloniaProperty.Register<MaterialDialog, string>(nameof(PrimaryActionText), "确认");

    public static readonly StyledProperty<string> CancelActionTextProperty =
        AvaloniaProperty.Register<MaterialDialog, string>(nameof(CancelActionText), "取消");

    public static readonly StyledProperty<bool> DismissOnScrimClickProperty =
        AvaloniaProperty.Register<MaterialDialog, bool>(nameof(DismissOnScrimClick), true);

    public static readonly StyledProperty<bool> DefaultFocusCancelProperty =
        AvaloniaProperty.Register<MaterialDialog, bool>(nameof(DefaultFocusCancel), false);

    public static readonly StyledProperty<CornerRadius> DialogCornerRadiusProperty =
        AvaloniaProperty.Register<MaterialDialog, CornerRadius>(
            nameof(DialogCornerRadius),
            new CornerRadius(28));

    private Border? _scrim;
    private Button? _primaryButton;
    private Button? _cancelButton;

    public MaterialDialog()
    {
        UpdateOpenState();
    }

    public event EventHandler? PrimaryActionClicked;

    public event EventHandler? Canceled;

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public string PrimaryActionText
    {
        get => GetValue(PrimaryActionTextProperty);
        set => SetValue(PrimaryActionTextProperty, value);
    }

    public string CancelActionText
    {
        get => GetValue(CancelActionTextProperty);
        set => SetValue(CancelActionTextProperty, value);
    }

    public bool DismissOnScrimClick
    {
        get => GetValue(DismissOnScrimClickProperty);
        set => SetValue(DismissOnScrimClickProperty, value);
    }

    public bool DefaultFocusCancel
    {
        get => GetValue(DefaultFocusCancelProperty);
        set => SetValue(DefaultFocusCancelProperty, value);
    }

    public CornerRadius DialogCornerRadius
    {
        get => GetValue(DialogCornerRadiusProperty);
        set => SetValue(DialogCornerRadiusProperty, value);
    }

    /// <summary>Closes the dialog and raises <see cref="PrimaryActionClicked"/>.</summary>
    public void InvokePrimaryAction()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        PrimaryActionClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the dialog and raises <see cref="Canceled"/>.</summary>
    public void InvokeCancel()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Canceled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies scrim tap dismissal when <see cref="DismissOnScrimClick"/> is enabled.</summary>
    public void InvokeScrimDismiss()
    {
        if (DismissOnScrimClick)
        {
            InvokeCancel();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scrim is not null)
        {
            _scrim.PointerPressed -= OnScrimPointerPressed;
        }
        if (_primaryButton is not null)
        {
            _primaryButton.Click -= OnPrimaryButtonClicked;
        }
        if (_cancelButton is not null)
        {
            _cancelButton.Click -= OnCancelButtonClicked;
        }

        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        _primaryButton = e.NameScope.Find<Button>("PART_PrimaryButton");
        _cancelButton = e.NameScope.Find<Button>("PART_CancelButton");

        if (_scrim is not null)
        {
            _scrim.PointerPressed += OnScrimPointerPressed;
        }
        if (_primaryButton is not null)
        {
            _primaryButton.Click += OnPrimaryButtonClicked;
        }
        if (_cancelButton is not null)
        {
            _cancelButton.Click += OnCancelButtonClicked;
        }

        ApplyDefaultFocus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsOpen && e.Key == Key.Escape && DismissOnScrimClick)
        {
            InvokeCancel();
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            UpdateOpenState();
            ApplyDefaultFocus();
        }
        else if (change.Property == BodyContentProperty)
        {
            if (change.OldValue is Control oldBody)
            {
                LogicalChildren.Remove(oldBody);
            }

            if (change.NewValue is Control newBody)
            {
                LogicalChildren.Add(newBody);
            }
        }
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) => InvokeScrimDismiss();

    private void OnPrimaryButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => InvokePrimaryAction();

    private void OnCancelButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => InvokeCancel();

    private void UpdateOpenState()
    {
        PseudoClasses.Set(":open", IsOpen);
        IsVisible = IsOpen;
    }

    private void ApplyDefaultFocus()
    {
        if (!IsOpen)
        {
            return;
        }

        var target = DefaultFocusCancel ? _cancelButton : _primaryButton;
        target?.Focus();
    }
}
