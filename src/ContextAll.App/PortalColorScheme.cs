using Avalonia.Styling;

namespace ContextAll.App;

public static class PortalColorScheme
{
    public static ThemeVariant Variant(int? colorScheme) =>
        colorScheme == 1 ? ThemeVariant.Dark : ThemeVariant.Light;
}
