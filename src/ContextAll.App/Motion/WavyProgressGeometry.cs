using Avalonia;

namespace ContextAll.App.Motion;

public sealed record WavyProgressLayout(
    IReadOnlyList<Point> ActiveWavePoints,
    double ActiveEndX,
    double InactiveTrackStartX,
    double InactiveTrackEndX,
    bool HasGap,
    Point StopDotCenter,
    double StopDotRadius);

/// <summary>Computes MD3 Expressive LinearWavyProgressIndicator geometry (sine wave + 4dp physical gap + stop dot).</summary>
public static class WavyProgressGeometry
{
    public const double PhysicalGapDp = 4.0;
    public const double DefaultWavelengthDp = 28.0;

    public static WavyProgressLayout Compute(
        double width,
        double height,
        double fraction,
        double phaseRadians,
        double strokeThickness = 4.0,
        double amplitudeScale = 1.0)
    {
        var clampedWidth = Math.Max(1.0, width);
        var clampedHeight = Math.Max(4.0, height);
        var clampedFraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0.0, 0.0, 1.0);
        var capInset = strokeThickness * 0.5;
        var centerY = clampedHeight * 0.5;
        var stopRadius = Math.Clamp(strokeThickness * 0.5, 1.5, 3.0);

        var maxAmplitude = Math.Max(0.0, (clampedHeight - strokeThickness) * 0.48) * Math.Clamp(amplitudeScale, 0.0, 1.0);
        var activeEndX = clampedFraction * clampedWidth;

        // Reduce wave amplitude near 0% and 100% per Material 3 wavy progress indicator spec.
        var edgeTaper = clampedFraction <= 0.0 || clampedFraction >= 1.0
            ? 0.0
            : Math.Min(1.0, Math.Min(clampedFraction / 0.08, (1.0 - clampedFraction) / 0.08));
        var effectiveAmplitude = maxAmplitude * edgeTaper;

        var points = new List<Point>();
        if (clampedFraction > 0.0 && activeEndX > capInset)
        {
            var startX = Math.Min(capInset, activeEndX * 0.25);
            var spanX = Math.Max(0.0, activeEndX - startX);
            var stepCount = Math.Clamp((int)Math.Ceiling(spanX / 2.5), 12, 160);
            var k = (2.0 * Math.PI) / DefaultWavelengthDp;

            for (var i = 0; i <= stepCount; i++)
            {
                var u = (double)i / stepCount;
                var x = startX + u * spanX;
                var y = centerY + effectiveAmplitude * Math.Sin(k * x - phaseRadians);
                points.Add(new Point(x, y));
            }
        }

        var hasGap = clampedFraction > 0.0 && clampedFraction < 1.0 && (clampedWidth - activeEndX) > PhysicalGapDp + capInset;
        var inactiveStartX = hasGap
            ? activeEndX + PhysicalGapDp
            : clampedFraction <= 0.0 ? capInset : clampedWidth - capInset;
        var inactiveEndX = Math.Max(inactiveStartX, clampedWidth - capInset);
        var stopDotCenter = new Point(Math.Max(stopRadius, clampedWidth - stopRadius - 1.0), centerY);

        return new WavyProgressLayout(
            points,
            activeEndX,
            inactiveStartX,
            inactiveEndX,
            hasGap,
            stopDotCenter,
            stopRadius);
    }
}
