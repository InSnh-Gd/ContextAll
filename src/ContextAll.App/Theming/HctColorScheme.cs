using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ContextAll.App.Theming;

public sealed record Md3ePalette(
    Color Seed,
    ThemeVariant Variant,
    Color Primary,
    Color OnPrimary,
    Color PrimaryContainer,
    Color OnPrimaryContainer,
    Color Secondary,
    Color OnSecondary,
    Color SecondaryContainer,
    Color OnSecondaryContainer,
    Color Tertiary,
    Color OnTertiary,
    Color TertiaryContainer,
    Color OnTertiaryContainer,
    Color Error,
    Color OnError,
    Color ErrorContainer,
    Color OnErrorContainer,
    Color Background,
    Color OnBackground,
    Color Surface,
    Color SurfaceDim,
    Color SurfaceBright,
    Color SurfaceContainerLowest,
    Color SurfaceContainerLow,
    Color SurfaceContainer,
    Color SurfaceContainerHigh,
    Color SurfaceContainerHighest,
    Color OnSurface,
    Color OnSurfaceVariant,
    Color Outline,
    Color OutlineVariant,
    Color Scrim,
    Color InverseSurface,
    Color InverseOnSurface,
    Color InversePrimary,
    Color SurfaceTint,
    Color OnSurfaceStrong,
    Color PrimaryFixed,
    Color PrimaryFixedDim,
    Color OnPrimaryFixed,
    Color OnPrimaryFixedVariant,
    Color SecondaryFixed,
    Color SecondaryFixedDim,
    Color OnSecondaryFixed,
    Color OnSecondaryFixedVariant,
    Color TertiaryFixed,
    Color TertiaryFixedDim,
    Color OnTertiaryFixed,
    Color OnTertiaryFixedVariant,
    Color Success,
    Color OnSuccess,
    Color SuccessContainer,
    Color OnSuccessContainer,
    Color Warning,
    Color OnWarning,
    Color WarningContainer,
    Color OnWarningContainer,
    Color Info,
    Color OnInfo,
    Color InfoContainer,
    Color OnInfoContainer)
{
    public void ApplyTo(IResourceDictionary resources, string? uiFontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        SetColorAndBrush(resources, "Md3Primary", Primary);
        SetColorAndBrush(resources, "Md3OnPrimary", OnPrimary);
        SetColorAndBrush(resources, "Md3PrimaryContainer", PrimaryContainer);
        SetColorAndBrush(resources, "Md3OnPrimaryContainer", OnPrimaryContainer);
        SetColorAndBrush(resources, "Md3PrimaryFixed", PrimaryFixed);
        SetColorAndBrush(resources, "Md3PrimaryFixedDim", PrimaryFixedDim);
        SetColorAndBrush(resources, "Md3OnPrimaryFixed", OnPrimaryFixed);
        SetColorAndBrush(resources, "Md3OnPrimaryFixedVariant", OnPrimaryFixedVariant);

        SetColorAndBrush(resources, "Md3Secondary", Secondary);
        SetColorAndBrush(resources, "Md3OnSecondary", OnSecondary);
        SetColorAndBrush(resources, "Md3SecondaryContainer", SecondaryContainer);
        SetColorAndBrush(resources, "Md3OnSecondaryContainer", OnSecondaryContainer);
        SetColorAndBrush(resources, "Md3SecondaryFixed", SecondaryFixed);
        SetColorAndBrush(resources, "Md3SecondaryFixedDim", SecondaryFixedDim);
        SetColorAndBrush(resources, "Md3OnSecondaryFixed", OnSecondaryFixed);
        SetColorAndBrush(resources, "Md3OnSecondaryFixedVariant", OnSecondaryFixedVariant);

        SetColorAndBrush(resources, "Md3Tertiary", Tertiary);
        SetColorAndBrush(resources, "Md3OnTertiary", OnTertiary);
        SetColorAndBrush(resources, "Md3TertiaryContainer", TertiaryContainer);
        SetColorAndBrush(resources, "Md3OnTertiaryContainer", OnTertiaryContainer);
        SetColorAndBrush(resources, "Md3TertiaryFixed", TertiaryFixed);
        SetColorAndBrush(resources, "Md3TertiaryFixedDim", TertiaryFixedDim);
        SetColorAndBrush(resources, "Md3OnTertiaryFixed", OnTertiaryFixed);
        SetColorAndBrush(resources, "Md3OnTertiaryFixedVariant", OnTertiaryFixedVariant);

        SetColorAndBrush(resources, "Md3Error", Error);
        SetColorAndBrush(resources, "Md3OnError", OnError);
        SetColorAndBrush(resources, "Md3ErrorContainer", ErrorContainer);
        SetColorAndBrush(resources, "Md3OnErrorContainer", OnErrorContainer);

        SetColorAndBrush(resources, "Md3Success", Success);
        SetColorAndBrush(resources, "Md3OnSuccess", OnSuccess);
        SetColorAndBrush(resources, "Md3SuccessContainer", SuccessContainer);
        SetColorAndBrush(resources, "Md3OnSuccessContainer", OnSuccessContainer);
        SetColorAndBrush(resources, "Md3Warning", Warning);
        SetColorAndBrush(resources, "Md3OnWarning", OnWarning);
        SetColorAndBrush(resources, "Md3WarningContainer", WarningContainer);
        SetColorAndBrush(resources, "Md3OnWarningContainer", OnWarningContainer);
        SetColorAndBrush(resources, "Md3Info", Info);
        SetColorAndBrush(resources, "Md3OnInfo", OnInfo);
        SetColorAndBrush(resources, "Md3InfoContainer", InfoContainer);
        SetColorAndBrush(resources, "Md3OnInfoContainer", OnInfoContainer);

        SetColorAndBrush(resources, "Md3Background", Background);
        SetColorAndBrush(resources, "Md3OnBackground", OnBackground);
        SetColorAndBrush(resources, "Md3Surface", Surface);
        SetColorAndBrush(resources, "Md3SurfaceDim", SurfaceDim);
        SetColorAndBrush(resources, "Md3SurfaceBright", SurfaceBright);
        SetColorAndBrush(resources, "Md3SurfaceContainerLowest", SurfaceContainerLowest);
        SetColorAndBrush(resources, "Md3SurfaceContainerLow", SurfaceContainerLow);
        SetColorAndBrush(resources, "Md3SurfaceContainer", SurfaceContainer);
        SetColorAndBrush(resources, "Md3SurfaceContainerHigh", SurfaceContainerHigh);
        SetColorAndBrush(resources, "Md3SurfaceContainerHighest", SurfaceContainerHighest);
        SetColorAndBrush(resources, "Md3OnSurface", OnSurface);
        SetColorAndBrush(resources, "Md3OnSurfaceStrong", OnSurfaceStrong);
        SetColorAndBrush(resources, "Md3OnSurfaceVariant", OnSurfaceVariant);
        SetColorAndBrush(resources, "Md3Outline", Outline);
        SetColorAndBrush(resources, "Md3OutlineVariant", OutlineVariant);
        SetColorAndBrush(resources, "Md3Scrim", Scrim);
        SetColorAndBrush(resources, "Md3InverseSurface", InverseSurface);
        SetColorAndBrush(resources, "Md3InverseOnSurface", InverseOnSurface);
        SetColorAndBrush(resources, "Md3InversePrimary", InversePrimary);
        SetColorAndBrush(resources, "Md3SurfaceTint", SurfaceTint);

        resources["SystemAccentColor"] = Primary;
        resources["SystemAccentColorBrush"] = new SolidColorBrush(Primary);

        if (!string.IsNullOrWhiteSpace(uiFontFamily))
        {
            resources["Md3UiFontFamily"] = new FontFamily(uiFontFamily);
            resources["ContentControlThemeFontFamily"] = new FontFamily(uiFontFamily);
        }
    }

    static void SetColorAndBrush(IResourceDictionary resources, string tokenName, Color color)
    {
        resources[tokenName + "Color"] = color;
        resources[tokenName + "Brush"] = new SolidColorBrush(color);
    }
}

public static class HctColorScheme
{
    public static readonly Color DefaultBrandSeed = Color.FromRgb(0x1B, 0x5E, 0x91);

    // CAM16 viewing conditions for D65 sRGB standard display (Yw = 18.41865, Yb = ~18.4, F = 1.0, c = 0.69, Nc = 1.0)
    static readonly double[] D65WhitePoint = [95.047, 100.0, 108.883];
    const double AdaptationY = 11.725677948856951; // (200 / pi) * 18.418651851244416 / 100
    const double BackgroundLstar = 50.0;
    const double SurroundC = 0.69;
    const double SurroundNc = 1.0;
    const double N = 0.18418651851244416;
    const double Nbb = 1.0167978483992356; // 0.725 * (1/N)^0.2
    const double Ncb = 1.0167978483992356;
    const double Z = 1.909169568483652; // 1.48 + sqrt(N)
    const double Fl = 0.3884814537800353; // 0.2*k^4*(5*La) + 0.1*(1-k^4)^2*(5*La)^(1/3)
    static readonly double[] RgbD = [1.02117770275752, 0.9863077294280124, 0.9339605082802299];

    public static Md3ePalette Generate(Color? seedColor, ThemeVariant variant)
    {
        var seed = seedColor ?? DefaultBrandSeed;
        var isDark = variant == ThemeVariant.Dark;
        var (seedHue, seedChroma, _) = FromColor(seed);

        // Fidelity Expressive scheme: preserves seed Hue while boosting Expressive chromaticity.
        var primaryChroma = Math.Max(42.0, seedChroma);
        var secondaryChroma = 20.0;
        var tertiaryHue = SanitizeDegrees(seedHue + 60.0);
        var tertiaryChroma = 32.0;
        var neutralChroma = 8.0;
        var neutralVariantChroma = 12.0;
        const double errorHue = 25.0;
        const double errorChroma = 84.0;
        const double successHue = 142.0;
        const double successChroma = 52.0;
        const double warningHue = 76.0;
        const double warningChroma = 62.0;

        var background = Tone(seedHue, neutralChroma, isDark ? 6 : 98);
        var surface = background;
        var surfaceDim = Tone(seedHue, neutralChroma, isDark ? 6 : 87);
        var surfaceBright = Tone(seedHue, neutralChroma, isDark ? 24 : 98);
        var surfaceContainerLowest = Tone(seedHue, neutralChroma, isDark ? 4 : 100);
        var surfaceContainerLow = Tone(seedHue, neutralChroma, isDark ? 10 : 96);
        var surfaceContainer = Tone(seedHue, neutralChroma, isDark ? 12 : 94);
        var surfaceContainerHigh = Tone(seedHue, neutralChroma, isDark ? 17 : 92);
        var surfaceContainerHighest = Tone(seedHue, neutralChroma, isDark ? 22 : 90);

        var onSurface = EnsureContrast(seedHue, neutralChroma, isDark ? 90 : 10, surfaceContainer, preferLighter: isDark);
        var onSurfaceStrong = EnsureContrast(seedHue, neutralChroma, isDark ? 96 : 4, surfaceContainer, preferLighter: isDark);
        var onSurfaceVariant = EnsureContrast(seedHue, neutralVariantChroma, isDark ? 80 : 30, surfaceContainer, preferLighter: isDark);
        var outline = Tone(seedHue, neutralVariantChroma, isDark ? 60 : 50);
        var outlineVariant = Tone(seedHue, neutralVariantChroma, isDark ? 30 : 80);

        // Expressive light containers use vibrant Tone 30 foregrounds (FerroUI compose.material3:1.5.0-beta02).
        var primary = EnsureContrast(seedHue, primaryChroma, isDark ? 80 : 40, surface, preferLighter: isDark);
        var onPrimary = EnsureContrast(seedHue, primaryChroma, isDark ? 20 : 100, primary, preferLighter: !isDark);
        var primaryContainer = Tone(seedHue, primaryChroma, isDark ? 30 : 90);
        var onPrimaryContainer = EnsureContrast(seedHue, primaryChroma, isDark ? 90 : 30, primaryContainer, preferLighter: isDark);

        var secondary = EnsureContrast(seedHue, secondaryChroma, isDark ? 80 : 40, surface, preferLighter: isDark);
        var onSecondary = EnsureContrast(seedHue, secondaryChroma, isDark ? 20 : 100, secondary, preferLighter: !isDark);
        var secondaryContainer = Tone(seedHue, secondaryChroma, isDark ? 30 : 90);
        var onSecondaryContainer = EnsureContrast(seedHue, secondaryChroma, isDark ? 90 : 30, secondaryContainer, preferLighter: isDark);

        var tertiary = EnsureContrast(tertiaryHue, tertiaryChroma, isDark ? 80 : 40, surface, preferLighter: isDark);
        var onTertiary = EnsureContrast(tertiaryHue, tertiaryChroma, isDark ? 20 : 100, tertiary, preferLighter: !isDark);
        var tertiaryContainer = Tone(tertiaryHue, tertiaryChroma, isDark ? 30 : 90);
        var onTertiaryContainer = EnsureContrast(tertiaryHue, tertiaryChroma, isDark ? 90 : 30, tertiaryContainer, preferLighter: isDark);

        var error = EnsureContrast(errorHue, errorChroma, isDark ? 80 : 40, surface, preferLighter: isDark);
        var onError = EnsureContrast(errorHue, errorChroma, isDark ? 20 : 100, error, preferLighter: !isDark);
        var errorContainer = Tone(errorHue, errorChroma, isDark ? 30 : 90);
        var onErrorContainer = EnsureContrast(errorHue, errorChroma, isDark ? 90 : 30, errorContainer, preferLighter: isDark);

        var inverseSurface = Tone(seedHue, neutralChroma, isDark ? 90 : 20);
        var inverseOnSurface = EnsureContrast(seedHue, neutralChroma, isDark ? 20 : 95, inverseSurface, preferLighter: !isDark);
        var inversePrimary = Tone(seedHue, primaryChroma, isDark ? 40 : 80);

        // Fixed roles are invariant across light and dark variants.
        var primaryFixed = Tone(seedHue, primaryChroma, 90);
        var primaryFixedDim = Tone(seedHue, primaryChroma, 80);
        var onPrimaryFixed = EnsureContrast(seedHue, primaryChroma, 10, primaryFixed, preferLighter: false);
        var onPrimaryFixedVariant = EnsureContrast(seedHue, primaryChroma, 30, primaryFixedDim, preferLighter: false);

        var secondaryFixed = Tone(seedHue, secondaryChroma, 90);
        var secondaryFixedDim = Tone(seedHue, secondaryChroma, 80);
        var onSecondaryFixed = EnsureContrast(seedHue, secondaryChroma, 10, secondaryFixed, preferLighter: false);
        var onSecondaryFixedVariant = EnsureContrast(seedHue, secondaryChroma, 30, secondaryFixedDim, preferLighter: false);

        var tertiaryFixed = Tone(tertiaryHue, tertiaryChroma, 90);
        var tertiaryFixedDim = Tone(tertiaryHue, tertiaryChroma, 80);
        var onTertiaryFixed = EnsureContrast(tertiaryHue, tertiaryChroma, 10, tertiaryFixed, preferLighter: false);
        var onTertiaryFixedVariant = EnsureContrast(tertiaryHue, tertiaryChroma, 30, tertiaryFixedDim, preferLighter: false);

        // Harmonized semantic roles.
        var success = EnsureContrast(successHue, successChroma, isDark ? 78 : 38, surface, preferLighter: isDark);
        var onSuccess = EnsureContrast(successHue, successChroma, isDark ? 16 : 100, success, preferLighter: !isDark);
        var successContainer = Tone(successHue, successChroma, isDark ? 28 : 90);
        var onSuccessContainer = EnsureContrast(successHue, successChroma, isDark ? 90 : 30, successContainer, preferLighter: isDark);

        var warning = EnsureContrast(warningHue, warningChroma, isDark ? 80 : 42, surface, preferLighter: isDark);
        var onWarning = EnsureContrast(warningHue, warningChroma, isDark ? 14 : 100, warning, preferLighter: !isDark);
        var warningContainer = Tone(warningHue, warningChroma, isDark ? 28 : 90);
        var onWarningContainer = EnsureContrast(warningHue, warningChroma, isDark ? 90 : 30, warningContainer, preferLighter: isDark);

        return new Md3ePalette(
            seed,
            variant,
            primary,
            onPrimary,
            primaryContainer,
            onPrimaryContainer,
            secondary,
            onSecondary,
            secondaryContainer,
            onSecondaryContainer,
            tertiary,
            onTertiary,
            tertiaryContainer,
            onTertiaryContainer,
            error,
            onError,
            errorContainer,
            onErrorContainer,
            background,
            onSurface,
            surface,
            surfaceDim,
            surfaceBright,
            surfaceContainerLowest,
            surfaceContainerLow,
            surfaceContainer,
            surfaceContainerHigh,
            surfaceContainerHighest,
            onSurface,
            onSurfaceVariant,
            outline,
            outlineVariant,
            Color.FromArgb(0x52, 0x00, 0x00, 0x00),
            inverseSurface,
            inverseOnSurface,
            inversePrimary,
            primary,
            onSurfaceStrong,
            primaryFixed,
            primaryFixedDim,
            onPrimaryFixed,
            onPrimaryFixedVariant,
            secondaryFixed,
            secondaryFixedDim,
            onSecondaryFixed,
            onSecondaryFixedVariant,
            tertiaryFixed,
            tertiaryFixedDim,
            onTertiaryFixed,
            onTertiaryFixedVariant,
            success,
            onSuccess,
            successContainer,
            onSuccessContainer,
            warning,
            onWarning,
            warningContainer,
            onWarningContainer,
            primary,
            onPrimary,
            primaryContainer,
            onPrimaryContainer);
    }

    public static Color Tone(double hue, double chroma, double tone)
    {
        tone = Math.Clamp(tone, 0.0, 100.0);
        if (tone <= 0.0)
        {
            return Colors.Black;
        }

        if (tone >= 100.0)
        {
            return Colors.White;
        }

        if (chroma < 0.5)
        {
            var y = YFromLstar(tone);
            var srgb = LinearToSrgbByte(y / 100.0);
            return Color.FromRgb(srgb, srgb, srgb);
        }

        // Binary-search max achievable chroma in sRGB at this hue and tone, then solve exact tone at min(requestedChroma, maxChroma).
        var hRad = SanitizeDegrees(hue) * Math.PI / 180.0;
        var targetY = YFromLstar(tone);

        double lowC = 0.0;
        double highC = chroma;
        Color? bestColor = null;

        for (var step = 0; step < 18; step++)
        {
            var midC = step == 0 ? chroma : (lowC + highC) * 0.5;
            if (TrySolveCam16(hRad, midC, targetY, out var solved))
            {
                bestColor = solved;
                if (step == 0)
                {
                    break;
                }

                lowC = midC;
            }
            else
            {
                highC = midC;
            }
        }

        if (bestColor.HasValue)
        {
            return bestColor.Value;
        }

        var gray = LinearToSrgbByte(targetY / 100.0);
        return Color.FromRgb(gray, gray, gray);
    }

    public static (double Hue, double Chroma, double Tone) FromColor(Color color)
    {
        var rLin = SrgbByteToLinear(color.R) * 100.0;
        var gLin = SrgbByteToLinear(color.G) * 100.0;
        var bLin = SrgbByteToLinear(color.B) * 100.0;

        var x = 0.41233895 * rLin + 0.35762064 * gLin + 0.18051042 * bLin;
        var y = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
        var z = 0.01932141 * rLin + 0.11916382 * gLin + 0.95034478 * bLin;

        var rA = AdaptComponent((0.401288 * x + 0.650173 * y - 0.051461 * z) * RgbD[0]);
        var gA = AdaptComponent((-0.250268 * x + 1.204414 * y + 0.045854 * z) * RgbD[1]);
        var bA = AdaptComponent((-0.002079 * x + 0.048952 * y + 0.953127 * z) * RgbD[2]);

        var a = (11.0 * rA - 12.0 * gA + bA) / 11.0;
        var b = (rA + gA - 2.0 * bA) / 9.0;
        var u = (20.0 * rA + 20.0 * gA + 21.0 * bA) / 20.0;
        var p2 = (40.0 * rA + 20.0 * gA + bA) / 20.0;

        var hue = Math.Atan2(b, a) * 180.0 / Math.PI;
        hue = SanitizeDegrees(hue);

        var ac = p2 * Nbb;
        var aw = 29.01891663418193; // D65 achromatic response for BackgroundLstar=50, F=1
        var j = 100.0 * Math.Pow(ac / aw, SurroundC * Z);
        var huePrime = hue < 20.14 ? hue + 360.0 : hue;
        var eHue = 0.25 * (Math.Cos(huePrime * Math.PI / 180.0 + 2.0) + 3.8);
        var p1 = (50000.0 / 13.0) * eHue * SurroundNc * Ncb;
        var t = p1 * Math.Sqrt(a * a + b * b) / (u + 0.305);
        var alpha = Math.Pow(1.64 - Math.Pow(0.29, N), 0.73) * Math.Pow(t, 0.9);
        var chroma = alpha * Math.Sqrt(j / 100.0);

        return (hue, chroma, LstarFromY(y));
    }

    public static double ContrastRatio(Color foreground, Color background)
    {
        var l1 = RelativeLuminance(foreground);
        var l2 = RelativeLuminance(background);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(Color color)
    {
        var r = SrgbByteToLinear(color.R);
        var g = SrgbByteToLinear(color.G);
        var b = SrgbByteToLinear(color.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    static Color EnsureContrast(double hue, double chroma, double initialTone, Color background, bool preferLighter, double minRatio = 4.5)
    {
        var candidate = Tone(hue, chroma, initialTone);
        if (ContrastRatio(candidate, background) >= minRatio)
        {
            return candidate;
        }

        // Step away from background luminance first along the preferred direction, then opposite if clipped.
        var step = preferLighter ? 4.0 : -4.0;
        for (var t = initialTone + step; t is >= 0.0 and <= 100.0; t += step)
        {
            var probe = Tone(hue, chroma, t);
            if (ContrastRatio(probe, background) >= minRatio)
            {
                return probe;
            }
        }

        var boundary = preferLighter ? Colors.White : Colors.Black;
        if (ContrastRatio(boundary, background) >= minRatio)
        {
            return boundary;
        }

        var fallback = preferLighter ? Colors.Black : Colors.White;
        return ContrastRatio(fallback, background) >= ContrastRatio(boundary, background) ? fallback : boundary;
    }

    static bool TrySolveCam16(double hueRad, double chroma, double targetY, out Color color)
    {
        color = default;
        var lowJ = 0.0;
        var highJ = 100.0;

        for (var iter = 0; iter < 24; iter++)
        {
            var j = 0.5 * (lowJ + highJ);
            if (!TryCam16JchToLinearRgb(j, chroma, hueRad, out var rLin, out var gLin, out var bLin, out var y))
            {
                // Outside gamut because J is extreme or chroma is too high at this lightness.
                var estY = YFromLstar(j);
                if (estY < targetY)
                {
                    lowJ = j;
                }
                else
                {
                    highJ = j;
                }

                continue;
            }

            if (Math.Abs(y - targetY) < 0.05)
            {
                if (rLin is >= -1e-4 and <= 100.05 &&
                    gLin is >= -1e-4 and <= 100.05 &&
                    bLin is >= -1e-4 and <= 100.05)
                {
                    color = Color.FromRgb(
                        LinearToSrgbByte(Math.Clamp(rLin / 100.0, 0.0, 1.0)),
                        LinearToSrgbByte(Math.Clamp(gLin / 100.0, 0.0, 1.0)),
                        LinearToSrgbByte(Math.Clamp(bLin / 100.0, 0.0, 1.0)));
                    return true;
                }
            }

            if (y < targetY)
            {
                lowJ = j;
            }
            else
            {
                highJ = j;
            }
        }

        var midJ = 0.5 * (lowJ + highJ);
        if (TryCam16JchToLinearRgb(midJ, chroma, hueRad, out var rl, out var gl, out var bl, out var yy) &&
            Math.Abs(yy - targetY) < 1.0 &&
            rl is >= -0.05 and <= 100.05 &&
            gl is >= -0.05 and <= 100.05 &&
            bl is >= -0.05 and <= 100.05)
        {
            color = Color.FromRgb(
                LinearToSrgbByte(Math.Clamp(rl / 100.0, 0.0, 1.0)),
                LinearToSrgbByte(Math.Clamp(gl / 100.0, 0.0, 1.0)),
                LinearToSrgbByte(Math.Clamp(bl / 100.0, 0.0, 1.0)));
            return true;
        }

        return false;
    }

    static bool TryCam16JchToLinearRgb(
        double j,
        double c,
        double hRad,
        out double rLin,
        out double gLin,
        out double bLin,
        out double y)
    {
        var alpha = j <= 0.0 ? 0.0 : c / Math.Sqrt(j / 100.0);
        var t = Math.Pow(alpha / Math.Pow(1.64 - Math.Pow(0.29, N), 0.73), 1.0 / 0.9);
        var eHue = 0.25 * (Math.Cos(hRad + 2.0) + 3.8);
        var aw = 29.01891663418193;
        var a = aw * Math.Pow(j / 100.0, 1.0 / (SurroundC * Z));
        var p1 = (50000.0 / 13.0) * eHue * SurroundNc * Ncb;
        var p2 = a / Nbb;
        var gamma = 23.0 * (p2 + 0.305) * t / (23.0 * p1 + 11.0 * t * Math.Cos(hRad) + 108.0 * t * Math.Sin(hRad));
        var ca = gamma * Math.Cos(hRad);
        var cb = gamma * Math.Sin(hRad);

        var rA = (460.0 * p2 + 451.0 * ca + 288.0 * cb) / 1403.0;
        var gA = (460.0 * p2 - 891.0 * ca - 261.0 * cb) / 1403.0;
        var bA = (460.0 * p2 - 220.0 * ca - 6300.0 * cb) / 1403.0;

        if (!TryInvertAdapt(rA, RgbD[0], out var rC) ||
            !TryInvertAdapt(gA, RgbD[1], out var gC) ||
            !TryInvertAdapt(bA, RgbD[2], out var bC))
        {
            rLin = gLin = bLin = y = 0;
            return false;
        }

        // CAM16 CAT16^(-1) -> XYZ
        var x = 1.86206786 * rC - 1.01125463 * gC + 0.14918677 * bC;
        y = 0.38752654 * rC + 0.62144744 * gC - 0.00897398 * bC;
        var z = -0.01584150 * rC - 0.03412294 * gC + 1.04996444 * bC;

        // XYZ -> linear sRGB [0..100]
        rLin = 3.2413774792388685 * x - 1.5376652402851851 * y - 0.49885366846268053 * z;
        gLin = -0.9691452513005321 * x + 1.8758853451067872 * y + 0.04156585616912061 * z;
        bLin = 0.05562093689691305 * x - 0.20395524564742123 * y + 1.0571799111220335 * z;
        return double.IsFinite(y) && double.IsFinite(rLin) && double.IsFinite(gLin) && double.IsFinite(bLin);
    }

    static double AdaptComponent(double component)
    {
        var x = Math.Pow(Fl * Math.Abs(component) / 100.0, 0.42);
        return Math.Sign(component) * 400.0 * x / (x + 27.13);
    }

    static bool TryInvertAdapt(double adapted, double dScale, out double c)
    {
        var absA = Math.Abs(adapted);
        if (absA >= 400.0)
        {
            c = 0;
            return false;
        }

        var cBase = Math.Max(0.0, (27.13 * absA) / (400.0 - absA));
        var cVal = Math.Sign(adapted) * (100.0 / Fl) * Math.Pow(cBase, 1.0 / 0.42);
        c = cVal / dScale;
        return true;
    }

    static double SrgbByteToLinear(byte value)
    {
        var normalized = value / 255.0;
        return normalized <= 0.04045
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }

    static byte LinearToSrgbByte(double normalized)
    {
        var clamped = Math.Clamp(normalized, 0.0, 1.0);
        var srgb = clamped <= 0.0031308
            ? 12.92 * clamped
            : 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(srgb * 255.0), 0, 255);
    }

    static double YFromLstar(double lstar)
    {
        var fy = (lstar + 16.0) / 116.0;
        const double kappa = 24389.0 / 27.0;
        const double epsilon = 216.0 / 24389.0;
        var yNorm = lstar > kappa * epsilon ? fy * fy * fy : lstar / kappa;
        return yNorm * D65WhitePoint[1];
    }

    static double LstarFromY(double y)
    {
        var yNorm = y / D65WhitePoint[1];
        const double kappa = 24389.0 / 27.0;
        const double epsilon = 216.0 / 24389.0;
        return yNorm <= epsilon
            ? kappa * yNorm
            : 116.0 * Math.Cbrt(yNorm) - 16.0;
    }

    static double SanitizeDegrees(double degrees)
    {
        var m = degrees % 360.0;
        return m < 0.0 ? m + 360.0 : m;
    }
}
