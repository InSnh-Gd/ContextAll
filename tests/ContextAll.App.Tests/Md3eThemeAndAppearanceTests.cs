using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CCSWE.Avalonia.Material;
using ContextAll.App.Platform;
using ContextAll.App.Theming;

namespace ContextAll.App.Tests;

public sealed class Md3eThemeAndAppearanceTests
{
    [Fact]
    public void Accent_cascade_prefers_portal_then_kde_then_gnome_then_brand_default()
    {
        var portalColor = Color.FromRgb(0xAA, 0x22, 0x44);
        var kdeGlobals = "[General]\nAccentColor=61,174,233\n";
        var kdeColor = Color.FromRgb(61, 174, 233);
        var gnomeBlue = Color.FromRgb(0x35, 0x84, 0xE4);

        Assert.Equal(portalColor, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalAccentColor: portalColor,
            KdeGlobalsContent: kdeGlobals,
            GnomeAccentColor: "'orange'")).AccentSeed);

        Assert.Equal(kdeColor, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalAccentColor: null,
            KdeGlobalsContent: kdeGlobals,
            GnomeAccentColor: "'orange'")).AccentSeed);

        Assert.Equal(gnomeBlue, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalAccentColor: null,
            KdeGlobalsContent: null,
            GnomeAccentColor: "'blue'")).AccentSeed);

        Assert.Equal(HctColorScheme.DefaultBrandSeed, AppearanceResolver.Resolve(new AppearanceProbeData(
            GnomeAccentColor: "'default'")).AccentSeed);
    }

    [Theory]
    [InlineData(double.NaN, 0.5, 0.5)]
    [InlineData(0.5, double.PositiveInfinity, 0.5)]
    [InlineData(-0.1, 0.5, 0.5)]
    [InlineData(0.5, 1.01, 0.5)]
    public void TryParsePortalAccent_rejects_out_of_range_or_non_finite_triples(double r, double g, double b)
    {
        Assert.False(AppearanceResolver.TryParsePortalAccent(r, g, b, out _));
    }

    [Fact]
    public void TryParsePortalAccent_maps_unit_doubles_to_srgb_color()
    {
        Assert.True(AppearanceResolver.TryParsePortalAccent(0.0, 0.5, 1.0, out var color));
        Assert.Equal(Color.FromRgb(0, 128, 255), color);
    }

    [Fact]
    public void Color_scheme_cascade_respects_portal_gnome_kde_and_defaults_to_light()
    {
        var kdeDark = "[General]\nColorScheme=BreezeDark\n";
        var kdeDarkWindow = "[Colors:Window]\nBackgroundNormal=24,26,30\n";

        Assert.Equal(ThemeVariant.Dark, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: 1,
            GnomeColorScheme: "'prefer-light'")).Variant);

        Assert.Equal(ThemeVariant.Light, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: 2,
            GnomeColorScheme: "'prefer-dark'")).Variant);

        Assert.Equal(ThemeVariant.Light, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: 0,
            GnomeColorScheme: "'prefer-dark'")).Variant);

        Assert.Equal(ThemeVariant.Dark, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: null,
            GnomeColorScheme: "'prefer-dark'")).Variant);

        Assert.Equal(ThemeVariant.Dark, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: null,
            KdeGlobalsContent: kdeDark)).Variant);

        Assert.Equal(ThemeVariant.Dark, AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: null,
            KdeGlobalsContent: kdeDarkWindow)).Variant);

        Assert.Equal(ThemeVariant.Light, AppearanceResolver.Resolve(new AppearanceProbeData()).Variant);
    }

    [Fact]
    public void Fonts_resolve_system_ui_sans_and_keep_monospace_separate()
    {
        var snapshot = AppearanceResolver.Resolve(new AppearanceProbeData(
            GnomeFontName: "'Cantarell 11'",
            FontConfigMonoFamily: "JetBrains Mono"));

        Assert.StartsWith("Cantarell,", snapshot.UiFontFamily);
        Assert.DoesNotContain("JetBrains Mono", snapshot.UiFontFamily);
        Assert.Contains("JetBrains Mono", snapshot.MonospaceFontFamily);

        var kdeFont = AppearanceResolver.Resolve(new AppearanceProbeData(
            KdeGlobalsContent: "[General]\nfont=Noto Sans SC,10,-1,5,400,0,0,0,0,0\n"));
        Assert.StartsWith("Noto Sans SC,", kdeFont.UiFontFamily);
        Assert.Equal(AppearanceResolver.DefaultMonospaceFontFamily, kdeFont.MonospaceFontFamily);
    }

    [Fact]
    public void Corrupt_kdeglobals_falls_back_cleanly_without_throwing()
    {
        var resolved = AppearanceResolver.Resolve(new AppearanceProbeData(
            KdeGlobalsContent: "[Broken\nAccentColor=999,bad,-1\nColorScheme=\n[Colors:Window]\nBackgroundNormal=invalid"));

        Assert.Equal(HctColorScheme.DefaultBrandSeed, resolved.AccentSeed);
        Assert.Equal(ThemeVariant.Light, resolved.Variant);
    }

    [Theory]
    [InlineData(0x1B, 0x5E, 0x91)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xE6, 0x00)]
    [InlineData(0xED, 0x5B, 0x00)]
    [InlineData(0x91, 0x41, 0xAC)]
    public void Hct_palette_enforces_wcag_aa_contrast_for_light_and_dark_themes(byte r, byte g, byte b)
    {
        var seed = Color.FromRgb(r, g, b);
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var p = HctColorScheme.Generate(seed, variant);
            Assert.True(HctColorScheme.ContrastRatio(p.OnPrimary, p.Primary) >= 4.5, $"{variant} Primary contrast");
            Assert.True(HctColorScheme.ContrastRatio(p.OnPrimaryContainer, p.PrimaryContainer) >= 4.5, $"{variant} PrimaryContainer");
            Assert.True(HctColorScheme.ContrastRatio(p.OnSecondary, p.Secondary) >= 4.5, $"{variant} Secondary");
            Assert.True(HctColorScheme.ContrastRatio(p.OnSecondaryContainer, p.SecondaryContainer) >= 4.5, $"{variant} SecondaryContainer");
            Assert.True(HctColorScheme.ContrastRatio(p.OnTertiaryContainer, p.TertiaryContainer) >= 4.5, $"{variant} TertiaryContainer");
            Assert.True(HctColorScheme.ContrastRatio(p.OnError, p.Error) >= 4.5, $"{variant} Error");
            Assert.True(HctColorScheme.ContrastRatio(p.OnErrorContainer, p.ErrorContainer) >= 4.5, $"{variant} ErrorContainer");
            Assert.True(HctColorScheme.ContrastRatio(p.OnSurface, p.Surface) >= 4.5, $"{variant} Surface");
            Assert.True(HctColorScheme.ContrastRatio(p.OnSurface, p.SurfaceContainerLow) >= 4.5, $"{variant} SurfaceContainerLow");
            Assert.True(HctColorScheme.ContrastRatio(p.OnSurface, p.SurfaceContainerHigh) >= 4.5, $"{variant} SurfaceContainerHigh");
            Assert.True(HctColorScheme.ContrastRatio(p.OnSurfaceVariant, p.SurfaceContainerLow) >= 4.5, $"{variant} OnSurfaceVariant");
        }
    }

    [Fact]
    public async Task Coordinator_applies_initial_snapshot_and_updates_resources_on_live_change()
    {
        var initial = new LinuxAppearanceSnapshot(
            ThemeVariant.Light,
            Color.FromRgb(0x35, 0x84, 0xE4),
            "Cantarell, Noto Sans, sans-serif",
            AppearanceResolver.DefaultMonospaceFontFamily);
        var updated = new LinuxAppearanceSnapshot(
            ThemeVariant.Dark,
            Color.FromRgb(0xED, 0x5B, 0x00),
            "Noto Sans, Inter, sans-serif",
            AppearanceResolver.DefaultMonospaceFontFamily);

        await using var source = new FakeAppearanceSource(initial);
        var resources = new ResourceDictionary();
        ThemeVariant? activeVariant = null;
        await using var coordinator = new LinuxAppearanceCoordinator(
            source,
            v => activeVariant = v,
            resources);

        await coordinator.StartAsync();

        Assert.Equal(ThemeVariant.Light, activeVariant);
        var lightSurface = Assert.IsType<Color>(resources["Md3SurfaceColor"]);
        var lightPrimary = Assert.IsType<Color>(resources["Md3PrimaryColor"]);
        var uiFont = Assert.IsType<FontFamily>(resources["Md3UiFontFamily"]);
        Assert.Equal("Cantarell", uiFont.Name);
        Assert.Equal(["Cantarell", "Noto Sans", "sans-serif"], uiFont.FamilyNames);

        source.Emit(updated);

        Assert.Equal(ThemeVariant.Dark, activeVariant);
        var darkSurface = Assert.IsType<Color>(resources["Md3SurfaceColor"]);
        var darkPrimary = Assert.IsType<Color>(resources["Md3PrimaryColor"]);
        Assert.NotEqual(lightSurface, darkSurface);
        Assert.NotEqual(lightPrimary, darkPrimary);
        Assert.Equal(darkPrimary, Assert.IsType<Color>(resources["SystemAccentColor"]));
        Assert.Equal(updated.AccentSeed, HctColorScheme.Generate(updated.AccentSeed, ThemeVariant.Dark).Surface == darkSurface ? updated.AccentSeed : default);
    }

    [Fact]
    public void Hct_palette_provides_48_roles_including_fixed_inverse_semantic_and_expressive_tone30_containers()
    {
        var light = HctColorScheme.Generate(Color.FromRgb(0x35, 0x84, 0xE4), ThemeVariant.Light);
        var dark = HctColorScheme.Generate(Color.FromRgb(0x35, 0x84, 0xE4), ThemeVariant.Dark);

        // Fixed roles must exist and remain invariant across light/dark.
        Assert.Equal(light.PrimaryFixed, dark.PrimaryFixed);
        Assert.Equal(light.PrimaryFixedDim, dark.PrimaryFixedDim);
        Assert.Equal(light.OnPrimaryFixed, dark.OnPrimaryFixed);
        Assert.Equal(light.SecondaryFixed, dark.SecondaryFixed);
        Assert.Equal(light.TertiaryFixed, dark.TertiaryFixed);

        // Inverse + SurfaceTint + OnSurfaceStrong + semantic containers.
        Assert.NotEqual(light.InverseSurface, light.Surface);
        Assert.True(HctColorScheme.ContrastRatio(light.InverseOnSurface, light.InverseSurface) >= 4.5);
        Assert.True(HctColorScheme.ContrastRatio(dark.InverseOnSurface, dark.InverseSurface) >= 4.5);
        Assert.True(HctColorScheme.ContrastRatio(light.OnSuccessContainer, light.SuccessContainer) >= 4.5);
        Assert.True(HctColorScheme.ContrastRatio(light.OnWarningContainer, light.WarningContainer) >= 4.5);
        Assert.True(HctColorScheme.ContrastRatio(light.OnInfoContainer, light.InfoContainer) >= 4.5);
        Assert.Equal(light.Primary, light.SurfaceTint);

        // Light container foregrounds target Tone ~30 (distinctly richer than pure black/near-black).
        var (_, _, tone30ContainerFg) = HctColorScheme.FromColor(light.OnPrimaryContainer);
        Assert.InRange(tone30ContainerFg, 18.0, 36.0);
    }

    [Fact]
    public void AppearanceResolver_honors_manual_mode_and_accent_overrides_and_builds_cjk_mixed_fonts()
    {
        var probe = new AppearanceProbeData(
            PortalColorScheme: 0, // Prefer Light from desktop
            PortalAccentColor: Color.FromRgb(0xED, 0x5B, 0x00),
            GnomeFontName: "'Cantarell 11'");

        var overridden = AppearanceResolver.Resolve(
            probe,
            AppearanceModeOverride.Dark,
            AccentSourceOverride.ForestTeal);

        Assert.Equal(ThemeVariant.Dark, overridden.Variant);
        Assert.Equal(AppearanceResolver.GetPresetAccent(AccentSourceOverride.ForestTeal), overridden.AccentSeed);
        Assert.Contains("Plus Jakarta Sans", overridden.BrandFontFamily);
        Assert.Contains("Cantarell", overridden.BrandFontFamily);
        Assert.Contains("Noto Sans CJK SC", overridden.BrandFontFamily);
        Assert.Contains("DM Sans", overridden.PlainFontFamily);
        Assert.Contains("Noto Sans CJK SC", overridden.PlainFontFamily);
    }

    [Fact]
    public void Md3eThemeAdapter_syncs_unprefixed_ccswe_light_and_dark_theme_dictionaries_and_fonts()
    {
        var resources = new ResourceDictionary();
        var lightPalette = HctColorScheme.Generate(Color.FromRgb(0x91, 0x41, 0xAC), ThemeVariant.Light);
        var darkPalette = HctColorScheme.Generate(Color.FromRgb(0x91, 0x41, 0xAC), ThemeVariant.Dark);
        var snapshot = new LinuxAppearanceSnapshot(
            ThemeVariant.Dark,
            Color.FromRgb(0x91, 0x41, 0xAC),
            "Cantarell, Noto Sans, sans-serif",
            AppearanceResolver.DefaultMonospaceFontFamily);

        Md3eThemeAdapter.ApplyToResourceDictionary(resources, null, snapshot, lightPalette, darkPalette);

        Assert.True(resources.ThemeDictionaries.TryGetValue(ThemeVariant.Light, out var lightProvider));
        Assert.True(resources.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out var darkProvider));

        var lightDict = Assert.IsAssignableFrom<IResourceDictionary>(lightProvider);
        var darkDict = Assert.IsAssignableFrom<IResourceDictionary>(darkProvider);

        Assert.Equal(lightPalette.Primary, Assert.IsType<Color>(lightDict["PrimaryColor"]));
        Assert.Equal(lightPalette.SurfaceContainer, Assert.IsType<Color>(lightDict["SurfaceContainerColor"]));
        Assert.Equal(darkPalette.Primary, Assert.IsType<Color>(darkDict["PrimaryColor"]));
        Assert.Equal(darkPalette.SurfaceContainer, Assert.IsType<Color>(darkDict["SurfaceContainerColor"]));

        var lightPrimaryBrush = Assert.IsAssignableFrom<ISolidColorBrush>(lightDict["Primary"]);
        var darkPrimaryBrush = Assert.IsAssignableFrom<ISolidColorBrush>(darkDict["Primary"]);
        Assert.Equal(lightPalette.Primary, lightPrimaryBrush.Color);
        Assert.Equal(darkPalette.Primary, darkPrimaryBrush.Color);

        // State-layer translucent brushes present
        Assert.True(lightDict.ContainsKey("OnSurfaceHover"));
        Assert.True(lightDict.ContainsKey("PrimaryHover"));
        Assert.True(lightDict.ContainsKey("PrimaryPressed"));

        // Composite Brand + Body font families injected into CCSWE standard keys
        var brandFont = Assert.IsType<FontFamily>(resources["BrandFontFamily"]);
        var bodyFont = Assert.IsType<FontFamily>(resources["BodyFontFamily"]);
        Assert.Contains("Plus Jakarta Sans", brandFont.ToString());
        Assert.Contains("DM Sans", bodyFont.ToString());

        // Legacy Md3* aliases still synchronized on top-level resources for active variant
        Assert.Equal(darkPalette.Primary, Assert.IsType<Color>(resources["Md3PrimaryColor"]));
    }

    private sealed class FakeAppearanceSource(LinuxAppearanceSnapshot initial) : ILinuxAppearanceSource
    {
        public event Action<LinuxAppearanceSnapshot>? Changed;

        public Task<LinuxAppearanceSnapshot> StartAsync(CancellationToken ct = default) =>
            Task.FromResult(initial);

        public void Emit(LinuxAppearanceSnapshot next) => Changed?.Invoke(next);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
