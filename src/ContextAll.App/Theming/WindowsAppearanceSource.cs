using System.Runtime.Versioning;
using Avalonia.Media;

namespace ContextAll.App.Theming;

/// <summary>Maps Windows Apps light/dark (and optional accent) onto the shared HCT appearance snapshot.</summary>
public sealed class WindowsAppearanceSource : ILinuxAppearanceSource
{
    private readonly Func<AppearanceProbeData> _probe;

    public WindowsAppearanceSource(Func<AppearanceProbeData>? probe = null)
    {
        _probe = probe ?? ReadSystemProbe;
    }

#pragma warning disable CS0067
    public event Action<LinuxAppearanceSnapshot>? Changed;
#pragma warning restore CS0067

    public static LinuxAppearanceSnapshot Resolve(bool appsUseLightTheme, Color? accentColor = null) =>
        AppearanceResolver.Resolve(new AppearanceProbeData(
            PortalColorScheme: appsUseLightTheme ? 0u : 1u,
            PortalAccentColor: accentColor,
            FontConfigSansFamily: "Segoe UI",
            FontConfigMonoFamily: "Cascadia Mono"));

    public Task<LinuxAppearanceSnapshot> StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = AppearanceResolver.Resolve(_probe());
        return Task.FromResult(snapshot);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static AppearanceProbeData ReadSystemProbe()
    {
        var light = true;
        Color? accent = null;
        if (OperatingSystem.IsWindows())
        {
            light = ReadAppsUseLightTheme();
            accent = ReadDwmAccent();
        }

        return new AppearanceProbeData(
            PortalColorScheme: light ? 0u : 1u,
            PortalAccentColor: accent,
            FontConfigSansFamily: "Segoe UI",
            FontConfigMonoFamily: "Cascadia Mono");
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
            {
                return i != 0;
            }
        }
        catch
        {
            // Fall through to light.
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static Color? ReadDwmAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            var value = key?.GetValue("ColorizationColor");
            if (value is int argb)
            {
                var unsigned = unchecked((uint)argb);
                return Color.FromRgb(
                    (byte)((unsigned >> 16) & 0xFF),
                    (byte)((unsigned >> 8) & 0xFF),
                    (byte)(unsigned & 0xFF));
            }
        }
        catch
        {
            // Brand seed via AppearanceResolver.
        }

        return null;
    }
}
