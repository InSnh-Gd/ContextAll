using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using ContextAll.App.Chrome;

namespace ContextAll.App.Controls;

/// <summary>
/// Clean borderless 48dp integrated CSD header bar hosting <see cref="ContextAllLogo"/>, title/subtitle,
/// and desktop-native GNOME/KDE window action buttons without any center status pill.
/// </summary>
[TemplatePart("PART_LeftButtons", typeof(StackPanel))]
[TemplatePart("PART_RightButtons", typeof(StackPanel))]
[TemplatePart("PART_DragRegion", typeof(Border))]
[TemplatePart("PART_Logo", typeof(ContextAllLogo))]
public class IntegratedHeaderBar : TemplatedControl
{
    public const double DefaultHeaderHeight = 48.0;

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<IntegratedHeaderBar, string>(nameof(TitleText), "ContextAll");

    public static readonly StyledProperty<string> SubtitleTextProperty =
        AvaloniaProperty.Register<IntegratedHeaderBar, string>(nameof(SubtitleText), string.Empty);

    public static readonly StyledProperty<WindowButtonLayout> ButtonLayoutProperty =
        AvaloniaProperty.Register<IntegratedHeaderBar, WindowButtonLayout>(
            nameof(ButtonLayout),
            WindowButtonLayout.DefaultRight);

    public static readonly StyledProperty<double> HeaderHeightProperty =
        AvaloniaProperty.Register<IntegratedHeaderBar, double>(nameof(HeaderHeight), DefaultHeaderHeight);

    public static readonly StyledProperty<bool> IsWindowMaximizedProperty =
        AvaloniaProperty.Register<IntegratedHeaderBar, bool>(nameof(IsWindowMaximized), false);

    private StackPanel? _leftPanel;
    private StackPanel? _rightPanel;
    private Border? _dragRegion;
    private ContextAllLogo _logoControl = new() { Width = 22, Height = 22 };

    public event Action? MinimizeRequested;

    public event Action? MaximizeRestoreRequested;

    public event Action? CloseRequested;

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string SubtitleText
    {
        get => GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public WindowButtonLayout ButtonLayout
    {
        get => GetValue(ButtonLayoutProperty);
        set => SetValue(ButtonLayoutProperty, value);
    }

    public double HeaderHeight
    {
        get => GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public bool IsWindowMaximized
    {
        get => GetValue(IsWindowMaximizedProperty);
        set => SetValue(IsWindowMaximizedProperty, value);
    }

    public IReadOnlyList<WindowActionButton> LeftButtons => ButtonLayout.Left;

    public IReadOnlyList<WindowActionButton> RightButtons => ButtonLayout.Right;

    /// <summary>Vector brand emblem rendered at the leading side of the title cluster.</summary>
    public ContextAllLogo LogoControl => _logoControl;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_dragRegion is not null)
        {
            _dragRegion.PointerPressed -= OnDragPointerPressed;
            _dragRegion.DoubleTapped -= OnDragDoubleTapped;
        }

        _leftPanel = e.NameScope.Find<StackPanel>("PART_LeftButtons");
        _rightPanel = e.NameScope.Find<StackPanel>("PART_RightButtons");
        _dragRegion = e.NameScope.Find<Border>("PART_DragRegion");

        if (e.NameScope.Find<ContextAllLogo>("PART_Logo") is { } templateLogo)
        {
            _logoControl = templateLogo;
        }

        if (_dragRegion is not null)
        {
            _dragRegion.PointerPressed += OnDragPointerPressed;
            _dragRegion.DoubleTapped += OnDragDoubleTapped;
        }

        RebuildActionButtons();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ButtonLayoutProperty || change.Property == IsWindowMaximizedProperty)
        {
            RebuildActionButtons();
        }
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnDragDoubleTapped(object? sender, TappedEventArgs e) =>
        MaximizeRestoreRequested?.Invoke();

    private void RebuildActionButtons()
    {
        PopulatePanel(_leftPanel, LeftButtons);
        PopulatePanel(_rightPanel, RightButtons);
    }

    private void PopulatePanel(StackPanel? panel, IReadOnlyList<WindowActionButton> buttons)
    {
        if (panel is null)
        {
            return;
        }

        panel.Children.Clear();
        panel.IsVisible = buttons.Count > 0;
        foreach (var kind in buttons)
        {
            panel.Children.Add(BuildCaptionButton(kind));
        }
    }

    private Button BuildCaptionButton(WindowActionButton kind)
    {
        var iconKey = kind switch
        {
            WindowActionButton.Minimize => "Material_IconMinimize",
            WindowActionButton.Maximize => IsWindowMaximized ? "Material_IconFilterNone" : "Material_IconCropSquare",
            _ => "Material_IconClose",
        };

        var icon = new PathIcon
        {
            Width = 14,
            Height = 14,
        };
        if (this.TryFindResource(iconKey, out var geoObj) && geoObj is Geometry geometry)
        {
            icon.Data = geometry;
        }
        else if (Application.Current is not null &&
                 Application.Current.TryFindResource(iconKey, out var appGeo) &&
                 appGeo is Geometry appGeometry)
        {
            icon.Data = appGeometry;
        }

        var button = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            Content = icon,
            Tag = kind,
        };
        button.Classes.Add("Icon");
        ToolTip.SetTip(button, kind switch
        {
            WindowActionButton.Minimize => "最小化",
            WindowActionButton.Maximize => IsWindowMaximized ? "还原" : "最大化",
            _ => "关闭",
        });

        button.Click += (_, _) =>
        {
            switch (kind)
            {
                case WindowActionButton.Minimize:
                    MinimizeRequested?.Invoke();
                    break;
                case WindowActionButton.Maximize:
                    MaximizeRestoreRequested?.Invoke();
                    break;
                case WindowActionButton.Close:
                    CloseRequested?.Invoke();
                    break;
            }
        };

        return button;
    }
}
