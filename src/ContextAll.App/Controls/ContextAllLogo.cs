using Avalonia;
using Avalonia.Controls.Primitives;

namespace ContextAll.App.Controls;

/// <summary>Vector brand mark matching Android ic_launcher_background.xml + ic_launcher_foreground.xml.</summary>
public sealed class ContextAllLogo : TemplatedControl
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
