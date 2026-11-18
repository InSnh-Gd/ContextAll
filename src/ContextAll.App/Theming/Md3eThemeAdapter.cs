using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CCSWE.Avalonia.Material;

namespace ContextAll.App.Theming;

/// <summary>Synchronizes 48-role HCT Expressive Light/Dark palettes and mixed CJK fonts into CCSWE ThemeDictionaries.</summary>
public static class Md3eThemeAdapter
{
    public static void ApplyToResourceDictionary(
        IResourceDictionary resources,
        MaterialTheme? materialTheme,
        LinuxAppearanceSnapshot snapshot,
        Md3ePalette lightPalette,
        Md3ePalette darkPalette)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(lightPalette);
        ArgumentNullException.ThrowIfNull(darkPalette);

        var brandFont = new FontFamily(snapshot.BrandFontFamily);
        var plainFont = new FontFamily(snapshot.PlainFontFamily);
        var activeResources = new ResourceDictionary
        {
            ["BrandFontFamily"] = brandFont,
            ["BodyFontFamily"] = plainFont,
            ["Md3BrandFontFamily"] = brandFont,
            ["Md3PlainFontFamily"] = plainFont,
            ["Md3UiFontFamily"] = new FontFamily(snapshot.UiFontFamily),
            ["Md3MonospaceFontFamily"] = new FontFamily(snapshot.MonospaceFontFamily),
            ["ContentControlThemeFontFamily"] = plainFont,
        };

        var lightDict = CreateVariantDictionary(lightPalette);
        var darkDict = CreateVariantDictionary(darkPalette);

        resources.ThemeDictionaries[ThemeVariant.Light] = lightDict;
        resources.ThemeDictionaries[ThemeVariant.Dark] = darkDict;

        if (materialTheme is not null)
        {
            ApplyToMaterialTheme(materialTheme, brandFont, plainFont, lightDict, darkDict);
        }

        // Mirror active variant tokens at root dictionary level for backwards-compatible lookups.
        var activeDict = snapshot.Variant == ThemeVariant.Dark ? darkDict : lightDict;
        activeResources.SetItems(activeDict);
        SetItems(resources, activeResources);
    }

    internal static bool TryApplyActiveVariant(IResourceDictionary resources, ThemeVariant variant)
    {
        var key = variant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (!resources.ThemeDictionaries.TryGetValue(key, out var provider) || provider is not ResourceDictionary active)
        {
            return false;
        }

        SetItems(resources, active);
        return true;
    }

    private static void ApplyToMaterialTheme(
        MaterialTheme materialTheme,
        FontFamily brandFont,
        FontFamily plainFont,
        ResourceDictionary lightDict,
        ResourceDictionary darkDict)
    {
        SetItems(materialTheme.Resources, new ResourceDictionary
        {
            ["BrandFontFamily"] = brandFont,
            ["BodyFontFamily"] = plainFont,
            ["ContentControlThemeFontFamily"] = plainFont,
        });
        var materialLight = new ResourceDictionary();
        materialLight.SetItems(lightDict);
        var materialDark = new ResourceDictionary();
        materialDark.SetItems(darkDict);
        materialTheme.Resources.ThemeDictionaries[ThemeVariant.Light] = materialLight;
        materialTheme.Resources.ThemeDictionaries[ThemeVariant.Dark] = materialDark;

        UpdateColorMergedDictionaries(materialTheme.Resources, lightDict, darkDict);
    }

    private static void UpdateColorMergedDictionaries(
        IResourceDictionary root,
        ResourceDictionary lightResources,
        ResourceDictionary darkResources)
    {
        foreach (var merged in root.MergedDictionaries)
        {
            if (merged is not IResourceDictionary child)
            {
                continue;
            }

            if (child.ThemeDictionaries.ContainsKey(ThemeVariant.Light) ||
                child.ThemeDictionaries.ContainsKey(ThemeVariant.Dark))
            {
                if (child.ThemeDictionaries.TryGetValue(ThemeVariant.Light, out var existingLight) &&
                    existingLight is IResourceDictionary lightDict &&
                    lightDict.ContainsKey("Primary"))
                {
                    SetItems(lightDict, lightResources);
                }

                if (child.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out var existingDark) &&
                    existingDark is IResourceDictionary darkDict &&
                    darkDict.ContainsKey("Primary"))
                {
                    SetItems(darkDict, darkResources);
                }
            }

            UpdateColorMergedDictionaries(child, lightResources, darkResources);
        }
    }

    private static void SetItems(IResourceDictionary target, ResourceDictionary values)
    {
        if (target is ResourceDictionary dictionary)
        {
            dictionary.SetItems(values);
        }
        else
        {
            foreach (var (key, value) in values)
            {
                target[key] = value;
            }
        }
    }

    private static ResourceDictionary CreateVariantDictionary(Md3ePalette palette)
    {
        var dict = new ResourceDictionary();
        PopulateDictionary(dict, palette);
        dict["SystemAccentColor"] = palette.Primary;
        dict["SystemAccentColorBrush"] = dict["Primary"];
        return dict;
    }

    public static void PopulateDictionary(IResourceDictionary dict, Md3ePalette p)
    {
        ArgumentNullException.ThrowIfNull(dict);
        ArgumentNullException.ThrowIfNull(p);

        SetToken(dict, "Primary", p.Primary);
        SetToken(dict, "OnPrimary", p.OnPrimary);
        SetToken(dict, "PrimaryContainer", p.PrimaryContainer);
        SetToken(dict, "OnPrimaryContainer", p.OnPrimaryContainer);
        SetToken(dict, "PrimaryFixed", p.PrimaryFixed);
        SetToken(dict, "PrimaryFixedDim", p.PrimaryFixedDim);
        SetToken(dict, "OnPrimaryFixed", p.OnPrimaryFixed);
        SetToken(dict, "OnPrimaryFixedVariant", p.OnPrimaryFixedVariant);

        SetToken(dict, "Secondary", p.Secondary);
        SetToken(dict, "OnSecondary", p.OnSecondary);
        SetToken(dict, "SecondaryContainer", p.SecondaryContainer);
        SetToken(dict, "OnSecondaryContainer", p.OnSecondaryContainer);
        SetToken(dict, "SecondaryFixed", p.SecondaryFixed);
        SetToken(dict, "SecondaryFixedDim", p.SecondaryFixedDim);
        SetToken(dict, "OnSecondaryFixed", p.OnSecondaryFixed);
        SetToken(dict, "OnSecondaryFixedVariant", p.OnSecondaryFixedVariant);

        SetToken(dict, "Tertiary", p.Tertiary);
        SetToken(dict, "OnTertiary", p.OnTertiary);
        SetToken(dict, "TertiaryContainer", p.TertiaryContainer);
        SetToken(dict, "OnTertiaryContainer", p.OnTertiaryContainer);
        SetToken(dict, "TertiaryFixed", p.TertiaryFixed);
        SetToken(dict, "TertiaryFixedDim", p.TertiaryFixedDim);
        SetToken(dict, "OnTertiaryFixed", p.OnTertiaryFixed);
        SetToken(dict, "OnTertiaryFixedVariant", p.OnTertiaryFixedVariant);

        SetToken(dict, "Error", p.Error);
        SetToken(dict, "OnError", p.OnError);
        SetToken(dict, "ErrorContainer", p.ErrorContainer);
        SetToken(dict, "OnErrorContainer", p.OnErrorContainer);

        SetToken(dict, "Success", p.Success);
        SetToken(dict, "OnSuccess", p.OnSuccess);
        SetToken(dict, "SuccessContainer", p.SuccessContainer);
        SetToken(dict, "OnSuccessContainer", p.OnSuccessContainer);

        SetToken(dict, "Warning", p.Warning);
        SetToken(dict, "OnWarning", p.OnWarning);
        SetToken(dict, "WarningContainer", p.WarningContainer);
        SetToken(dict, "OnWarningContainer", p.OnWarningContainer);

        SetToken(dict, "Info", p.Info);
        SetToken(dict, "OnInfo", p.OnInfo);
        SetToken(dict, "InfoContainer", p.InfoContainer);
        SetToken(dict, "OnInfoContainer", p.OnInfoContainer);

        SetToken(dict, "Background", p.Background);
        SetToken(dict, "OnBackground", p.OnBackground);
        SetToken(dict, "Surface", p.Surface);
        SetToken(dict, "SurfaceDim", p.SurfaceDim);
        SetToken(dict, "SurfaceBright", p.SurfaceBright);
        SetToken(dict, "SurfaceVariant", p.SurfaceContainerHighest);
        SetToken(dict, "SurfaceContainerLowest", p.SurfaceContainerLowest);
        SetToken(dict, "SurfaceContainerLow", p.SurfaceContainerLow);
        SetToken(dict, "SurfaceContainer", p.SurfaceContainer);
        SetToken(dict, "SurfaceContainerHigh", p.SurfaceContainerHigh);
        SetToken(dict, "SurfaceContainerHighest", p.SurfaceContainerHighest);
        SetToken(dict, "OnSurface", p.OnSurface);
        SetToken(dict, "OnSurfaceStrong", p.OnSurfaceStrong);
        SetToken(dict, "OnSurfaceVariant", p.OnSurfaceVariant);
        SetToken(dict, "Outline", p.Outline);
        SetToken(dict, "OutlineVariant", p.OutlineVariant);
        SetToken(dict, "Scrim", p.Scrim);
        SetToken(dict, "InverseSurface", p.InverseSurface);
        SetToken(dict, "InverseOnSurface", p.InverseOnSurface);
        SetToken(dict, "InversePrimary", p.InversePrimary);
        SetToken(dict, "SurfaceTint", p.SurfaceTint);

        // CCSWE built-in opacity masks + FerroUI state-layer brushes (hover 0.08, focus/pressed 0.10, drag 0.16)
        dict["OnSurface12"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.12));
        dict["OnSurface38"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.38));
        dict["Scrim32"] = new SolidColorBrush(WithAlpha(Colors.Black, 0.32));

        dict["PrimaryHover"] = new SolidColorBrush(WithAlpha(p.Primary, 0.08));
        dict["PrimaryFocus"] = new SolidColorBrush(WithAlpha(p.Primary, 0.10));
        dict["PrimaryPressed"] = new SolidColorBrush(WithAlpha(p.Primary, 0.10));
        dict["PrimaryRipple"] = new SolidColorBrush(WithAlpha(p.Primary, 0.12));

        dict["OnSurfaceHover"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.08));
        dict["OnSurfaceFocus"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.10));
        dict["OnSurfacePressed"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.10));
        dict["OnSurfaceDrag"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.16));
        dict["OnSurfaceRipple"] = new SolidColorBrush(WithAlpha(p.OnSurface, 0.12));

        dict["PrimaryContainerHover"] = new SolidColorBrush(WithAlpha(p.OnPrimaryContainer, 0.08));
        dict["PrimaryContainerPressed"] = new SolidColorBrush(WithAlpha(p.OnPrimaryContainer, 0.12));
        dict["SecondaryContainerHover"] = new SolidColorBrush(WithAlpha(p.OnSecondaryContainer, 0.08));
        dict["SecondaryContainerPressed"] = new SolidColorBrush(WithAlpha(p.OnSecondaryContainer, 0.12));
        dict["ErrorHover"] = new SolidColorBrush(WithAlpha(p.Error, 0.08));
        dict["ErrorPressed"] = new SolidColorBrush(WithAlpha(p.Error, 0.12));
    }

    private static void SetToken(IResourceDictionary dict, string name, Color color)
    {
        dict[name + "Color"] = color;
        dict[name] = new SolidColorBrush(color);
        dict["Md3" + name + "Color"] = color;
        dict["Md3" + name + "Brush"] = new SolidColorBrush(color);
    }

    private static Color WithAlpha(Color color, double opacity)
    {
        var a = (byte)Math.Clamp(Math.Round(opacity * 255.0), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }
}
