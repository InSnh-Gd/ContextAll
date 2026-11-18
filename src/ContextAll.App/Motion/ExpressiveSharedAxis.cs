using ContextAll.App.Controls;

namespace ContextAll.App.Motion;

public readonly record struct SharedAxisFrame(
    double IncomingOffset,
    double OutgoingOffset,
    double IncomingOpacity,
    double OutgoingOpacity);

/// <summary>
/// MD3E Expressive shared-axis X interpolation for peer section changes.
/// Spatial progress 0→1 maps a 30dp slide; opacity is a separate effects track.
/// </summary>
public static class ExpressiveSharedAxis
{
    public const double SlideDistance = 30.0;

    public static int Direction(ShellSection from, ShellSection to) =>
        Math.Sign((int)to - (int)from);

    public static SharedAxisFrame Interpolate(
        int direction,
        double spatialProgress,
        double incomingOpacity,
        double outgoingOpacity)
    {
        var travel = Math.Clamp(spatialProgress, 0.0, 1.0);
        var sign = direction == 0 ? 0 : Math.Sign(direction);
        return new SharedAxisFrame(
            IncomingOffset: sign * SlideDistance * (1.0 - travel),
            OutgoingOffset: -sign * SlideDistance * travel,
            IncomingOpacity: incomingOpacity,
            OutgoingOpacity: outgoingOpacity);
    }
}
