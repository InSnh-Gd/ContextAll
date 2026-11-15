using Avalonia;

namespace ContextAll.App.Motion;

/// <summary>7-shape Material 3 Expressive loading indicator polygon sampler and morpher in normalized [0,1]^2 coordinates.</summary>
public static class PolygonMorphGeometry
{
    public const int ShapeCount = 7;

    /// <summary>Returns ordered normalized vertices in [0,1]^2 centered at (0.5, 0.5) for shape index 0..6.</summary>
    public static IReadOnlyList<Point> SamplePolygon(int shapeIndex, int vertexCount = 72)
    {
        var count = Math.Max(12, vertexCount);
        var normalizedIndex = ((shapeIndex % ShapeCount) + ShapeCount) % ShapeCount;
        var pts = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var theta = (2.0 * Math.PI * i) / count;
            var (rx, ry) = ComputeRadii(normalizedIndex, theta);
            pts[i] = new Point(
                Math.Clamp(0.5 + rx * Math.Cos(theta), 0.0, 1.0),
                Math.Clamp(0.5 + ry * Math.Sin(theta), 0.0, 1.0));
        }

        return pts;
    }

    /// <summary>Interpolates smoothly between two M3E polygon shapes and applies rotation around (0.5, 0.5).</summary>
    public static IReadOnlyList<Point> Interpolate(
        int fromShape,
        int toShape,
        double progress,
        double rotationRadians = 0.0,
        int vertexCount = 72)
    {
        var t = Math.Clamp(double.IsFinite(progress) ? progress : 0.0, 0.0, 1.0);
        var a = SamplePolygon(fromShape, vertexCount);
        var b = SamplePolygon(toShape, vertexCount);
        var cosR = Math.Cos(rotationRadians);
        var sinR = Math.Sin(rotationRadians);

        var result = new Point[a.Count];
        for (var i = 0; i < a.Count; i++)
        {
            var nx = a[i].X + (b[i].X - a[i].X) * t - 0.5;
            var ny = a[i].Y + (b[i].Y - a[i].Y) * t - 0.5;

            var rx = nx * cosR - ny * sinR + 0.5;
            var ry = nx * sinR + ny * cosR + 0.5;
            result[i] = new Point(Math.Clamp(rx, 0.0, 1.0), Math.Clamp(ry, 0.0, 1.0));
        }

        return result;
    }

    private static (double Rx, double Ry) ComputeRadii(int shapeIndex, double theta) => shapeIndex switch
    {
        // 0: SoftBurst (10-petal soft star)
        0 => Uniform(0.39 + 0.07 * Math.Cos(10.0 * theta)),
        // 1: Cookie9Sided (9-lobe scalloped circle)
        1 => Uniform(0.41 + 0.055 * Math.Cos(9.0 * theta)),
        // 2: SoftPentagon (rounded 5-gon)
        2 => Uniform(SuperPolygonRadius(theta, sides: 5, sharpness: 0.14) * 0.45),
        // 3: Cookie6Sided (6-lobe symmetric scalloped blossom, 15-degree phase for equal X/Y bounds)
        3 => Uniform(0.40 + 0.055 * Math.Sin(6.0 * theta)),
        // 4: Sunny (8-ray rounded burst)
        4 => Uniform(0.38 + 0.085 * Math.Cos(8.0 * theta)),
        // 5: Cookie4Sided (4-lobe clover)
        5 => Uniform(0.40 + 0.075 * Math.Cos(4.0 * theta)),
        // 6: Squircle / Rounded Diamond loop
        _ => Uniform(SuperPolygonRadius(theta, sides: 4, sharpness: 0.20) * 0.45),
    };

    private static (double Rx, double Ry) Uniform(double r) => (r, r);

    private static double SuperPolygonRadius(double theta, int sides, double sharpness)
    {
        var sector = (2.0 * Math.PI) / sides;
        var halfSector = sector * 0.5;
        var local = ((theta % sector) + sector) % sector - halfSector;
        var edge = Math.Cos(halfSector) / Math.Max(0.25, Math.Cos(local));
        return 1.0 - sharpness + sharpness * (1.0 / Math.Max(0.75, edge));
    }
}
