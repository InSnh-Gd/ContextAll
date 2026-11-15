using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using ContextAll.App.Theming;

namespace ContextAll.App.Controls;

/// <summary>Resolves MD3E brushes and fonts dynamically with safe standalone fallbacks.</summary>
public static class Md3eResourceLookup
{
    private static readonly Md3ePalette LightFallback =
        HctColorScheme.Generate(HctColorScheme.DefaultBrandSeed, ThemeVariant.Light);

    public static IBrush ResolveBrush(StyledElement? context, string brushKey, Func<Md3ePalette, Color> fallbackSelector)
    {
        if (context is not null &&
            context.TryFindResource(brushKey, out var found) &&
            found is IBrush brush)
        {
            return brush;
        }

        if (Application.Current is not null &&
            Application.Current.TryFindResource(brushKey, out var appFound) &&
            appFound is IBrush appBrush)
        {
            return appBrush;
        }

        return new SolidColorBrush(fallbackSelector(LightFallback));
    }

    public static FontFamily ResolveUiFont(StyledElement? context) =>
        ResolveFont(context, "Md3UiFontFamily", AppearanceResolver.DefaultUiFontFamily);

    public static FontFamily ResolveMonospaceFont(StyledElement? context) =>
        ResolveFont(context, "Md3MonospaceFontFamily", AppearanceResolver.DefaultMonospaceFontFamily);

    private static FontFamily ResolveFont(StyledElement? context, string key, string fallback)
    {
        if (context is not null &&
            context.TryFindResource(key, out var found) &&
            found is FontFamily font)
        {
            return font;
        }

        if (Application.Current is not null &&
            Application.Current.TryFindResource(key, out var appFound) &&
            appFound is FontFamily appFont)
        {
            return appFont;
        }

        return new FontFamily(fallback);
    }

    public static void TrackThemeChanges(Control control, Action refresh)
    {
        EventHandler<ResourcesChangedEventArgs>? appResourcesHandler = null;

        control.AttachedToVisualTree += (_, _) =>
        {
            if (Application.Current is { } app && appResourcesHandler is null)
            {
                appResourcesHandler = (_, _) => refresh();
                app.ResourcesChanged += appResourcesHandler;
            }
            refresh();
        };
        control.DetachedFromVisualTree += (_, _) =>
        {
            if (Application.Current is { } app && appResourcesHandler is not null)
            {
                app.ResourcesChanged -= appResourcesHandler;
                appResourcesHandler = null;
            }
        };
        control.ActualThemeVariantChanged += (_, _) => refresh();
        control.ResourcesChanged += (_, _) => refresh();
    }

    public static Geometry? TryParseGeometry(string? pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData) || Application.Current is null)
        {
            return null;
        }

        try
        {
            return Geometry.Parse(pathData);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static void BindGeometry(PathIcon icon, string? pathData)
    {
        icon.Data = TryParseGeometry(pathData);
        if (icon.Data is null && !string.IsNullOrWhiteSpace(pathData))
        {
            icon.AttachedToVisualTree += (_, _) => icon.Data ??= TryParseGeometry(pathData);
        }
    }

    public static void TrySetHandCursor(InputElement control, bool hand)
    {
        if (Application.Current is null)
        {
            return;
        }

        try
        {
            control.Cursor = hand ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
        catch (InvalidOperationException)
        {
            // Standalone unit tests without an Avalonia platform windowing backend.
        }
    }
}
