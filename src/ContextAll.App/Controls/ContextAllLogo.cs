using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace ContextAll.App.Controls;

/// <summary>Vector brand mark matching Android ic_launcher_background.xml + ic_launcher_foreground.xml.</summary>
public sealed class ContextAllLogo : Viewbox
{
    public const string LeftDevicePathData =
        "M30,33h20c2.2,0 4,1.8 4,4v26c0,2.2 -1.8,4 -4,4H30c-2.2,0 -4,-1.8 -4,-4V37c0,-2.2 1.8,-4 4,-4z";

    public const string RightDevicePathData =
        "M58,41h20c2.2,0 4,1.8 4,4v26c0,2.2 -1.8,4 -4,4H58c-2.2,0 -4,-1.8 -4,-4V45c0,-2.2 1.8,-4 4,-4z";

    public const string BadgeCirclePathData =
        "M54,46c4.4,0 8,3.6 8,8s-3.6,8 -8,8s-8,-3.6 -8,-8S49.6,46 54,46z";

    public const string ArrowPathData =
        "M50,53h8v2h-8zM53,50l4,4l-4,4v-8z";

    public static readonly StyledProperty<double> LogoSizeProperty =
        AvaloniaProperty.Register<ContextAllLogo, double>(nameof(LogoSize), 28d);

    public ContextAllLogo()
    {
        Width = 28;
        Height = 28;
        Stretch = Stretch.Uniform;

        var backgroundBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#003547"), 0.0),
                new GradientStop(Color.Parse("#006684"), 1.0),
            ],
        };

        var canvas = new Canvas
        {
            Width = 108,
            Height = 108,
        };

        var rootClipper = new Border
        {
            Width = 108,
            Height = 108,
            CornerRadius = new CornerRadius(26),
            ClipToBounds = true,
            Background = backgroundBrush,
            Child = canvas,
        };

        _leftDevicePath = new PathShape { Fill = new SolidColorBrush(Color.Parse("#E0F7FF")) };
        _rightDevicePath = new PathShape { Fill = new SolidColorBrush(Color.Parse("#70D2FF")) };
        _badgeCirclePath = new PathShape { Fill = new SolidColorBrush(Color.Parse("#003547")) };
        _arrowPath = new PathShape { Fill = Brushes.White };

        canvas.Children.Add(_leftDevicePath);
        canvas.Children.Add(_rightDevicePath);
        canvas.Children.Add(_badgeCirclePath);
        canvas.Children.Add(_arrowPath);

        Child = rootClipper;
        EnsureGeometries();
    }

    private readonly PathShape _leftDevicePath;
    private readonly PathShape _rightDevicePath;
    private readonly PathShape _badgeCirclePath;
    private readonly PathShape _arrowPath;
    private bool _geometriesLoaded;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureGeometries();
    }

    private void EnsureGeometries()
    {
        if (_geometriesLoaded)
        {
            return;
        }

        _leftDevicePath.Data = Md3eResourceLookup.TryParseGeometry(LeftDevicePathData);
        _rightDevicePath.Data = Md3eResourceLookup.TryParseGeometry(RightDevicePathData);
        _badgeCirclePath.Data = Md3eResourceLookup.TryParseGeometry(BadgeCirclePathData);
        _arrowPath.Data = Md3eResourceLookup.TryParseGeometry(ArrowPathData);
        _geometriesLoaded = _leftDevicePath.Data is not null;
    }

    public double LogoSize
    {
        get => GetValue(LogoSizeProperty);
        set => SetValue(LogoSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LogoSizeProperty && double.IsFinite(LogoSize) && LogoSize > 0)
        {
            Width = LogoSize;
            Height = LogoSize;
        }
    }
}
