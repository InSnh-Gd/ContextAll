using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CCSWE.Avalonia.Material;
using ContextAll.App.Platform;
using Tmds.DBus.Protocol;

namespace ContextAll.App.Theming;

/// <summary>Snapshot of the resolved Linux desktop appearance settings.</summary>
public sealed record LinuxAppearanceSnapshot(
    ThemeVariant Variant,
    Color AccentSeed,
    string UiFontFamily,
    string MonospaceFontFamily)
{
    public const string CcsweBrandFontUri = "avares://ccswe.avalonia.material/Assets/Fonts/#Plus Jakarta Sans";
    public const string CcswePlainFontUri = "avares://ccswe.avalonia.material/Assets/Fonts/#DM Sans";
    public const string CjkFallbackFontChain = "Noto Sans CJK SC, Noto Sans CJK TC, WenQuanYi Micro Hei, sans-serif";

    public string BrandFontFamily => ComposeMixedFontFamily(CcsweBrandFontUri, UiFontFamily);
    public string PlainFontFamily => ComposeMixedFontFamily(CcswePlainFontUri, UiFontFamily);

    static string ComposeMixedFontFamily(string primaryEmbedded, string desktopSans)
    {
        var cleanDesktop = string.IsNullOrWhiteSpace(desktopSans)
            ? AppearanceResolver.DefaultUiFontFamily
            : desktopSans.Trim();
        return $"{primaryEmbedded}, {cleanDesktop}, {CjkFallbackFontChain}";
    }
}

/// <summary>Source for initial and live Linux desktop appearance updates.</summary>
public interface ILinuxAppearanceSource : IAsyncDisposable
{
    /// <summary>Raised when the resolved appearance snapshot changes.</summary>
    event Action<LinuxAppearanceSnapshot>? Changed;

    /// <summary>Reads the initial appearance snapshot and starts watching for changes.</summary>
    Task<LinuxAppearanceSnapshot> StartAsync(CancellationToken ct = default);
}

/// <summary>Raw appearance inputs collected from XDG Portal, KDE, GNOME, and fontconfig.</summary>
public sealed record AppearanceProbeData(
    uint? PortalColorScheme = null,
    Color? PortalAccentColor = null,
    string? GnomeColorScheme = null,
    string? GnomeAccentColor = null,
    string? GnomeFontName = null,
    string? KdeGlobalsContent = null,
    string? FontConfigSansFamily = null,
    string? FontConfigMonoFamily = null);

/// <summary>Pure resolver for Linux accent colors, theme variant, and UI/monospace fonts.</summary>
public static class AppearanceResolver
{
    public const string DefaultUiFontFamily = "Noto Sans, Inter, sans-serif";
    public const string DefaultMonospaceFontFamily = "JetBrains Mono, Noto Sans Mono, Monospace";

    private static readonly IReadOnlyDictionary<string, Color> GnomeAccentMap =
        new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = Color.FromRgb(0x35, 0x84, 0xE4),
            ["teal"] = Color.FromRgb(0x21, 0x90, 0xA4),
            ["green"] = Color.FromRgb(0x3A, 0x94, 0x4A),
            ["yellow"] = Color.FromRgb(0xC8, 0x88, 0x00),
            ["orange"] = Color.FromRgb(0xED, 0x5B, 0x00),
            ["red"] = Color.FromRgb(0xE6, 0x2D, 0x42),
            ["pink"] = Color.FromRgb(0xD5, 0x61, 0x99),
            ["purple"] = Color.FromRgb(0x91, 0x41, 0xAC),
            ["slate"] = Color.FromRgb(0x6F, 0x83, 0x96),
        };

    /// <summary>Resolves preset accent seed colors for manual accent overrides.</summary>
    public static Color GetPresetAccent(AccentSourceOverride source) => source switch
    {
        AccentSourceOverride.BrandBlue => HctColorScheme.DefaultBrandSeed,
        AccentSourceOverride.ForestTeal => Color.FromRgb(0x00, 0x6A, 0x60),
        AccentSourceOverride.SunsetTerracotta => Color.FromRgb(0xB3, 0x48, 0x28),
        AccentSourceOverride.VioletIndigo => Color.FromRgb(0x67, 0x50, 0xA4),
        _ => HctColorScheme.DefaultBrandSeed,
    };

    /// <summary>Resolves the full appearance snapshot using Portal -> KDE -> GNOME -> default cascades.</summary>
    public static LinuxAppearanceSnapshot Resolve(AppearanceProbeData probe) =>
        Resolve(probe, modeOverride: null, accentOverride: null);

    /// <summary>Resolves the full appearance snapshot with optional lightweight manual overrides.</summary>
    public static LinuxAppearanceSnapshot Resolve(
        AppearanceProbeData probe,
        AppearanceModeOverride? modeOverride,
        AccentSourceOverride? accentOverride)
    {
        var kde = ParseKdeGlobals(probe.KdeGlobalsContent);
        var variant = modeOverride switch
        {
            AppearanceModeOverride.Light => ThemeVariant.Light,
            AppearanceModeOverride.Dark => ThemeVariant.Dark,
            _ => ResolveThemeVariant(probe.PortalColorScheme, probe.GnomeColorScheme, kde.Variant),
        };
        var accent = accentOverride switch
        {
            AccentSourceOverride.BrandBlue
            or AccentSourceOverride.ForestTeal
            or AccentSourceOverride.SunsetTerracotta
            or AccentSourceOverride.VioletIndigo => GetPresetAccent(accentOverride.Value),
            _ => ResolveAccent(probe.PortalAccentColor, kde.AccentColor, probe.GnomeAccentColor),
        };
        var uiFont = ResolveUiFontFamily(probe.GnomeFontName, kde.UiFontFamily, probe.FontConfigSansFamily);
        var monoFont = ResolveMonospaceFontFamily(probe.FontConfigMonoFamily);
        return new LinuxAppearanceSnapshot(variant, accent, uiFont, monoFont);
    }

    /// <summary>Resolves accent seed color: XDG Portal -> KDE -> GNOME -> #1B5E91.</summary>
    public static Color ResolveAccent(
        Color? portalAccent,
        Color? kdeAccent,
        string? gnomeAccent)
    {
        if (portalAccent is { } p)
        {
            return p;
        }

        if (kdeAccent is { } k)
        {
            return k;
        }

        if (TryParseGnomeAccent(gnomeAccent, out var g))
        {
            return g;
        }

        return HctColorScheme.DefaultBrandSeed;
    }

    /// <summary>Resolves Light/Dark variant: XDG Portal -> GNOME -> KDE -> Light.</summary>
    public static ThemeVariant ResolveThemeVariant(
        uint? portalColorScheme,
        string? gnomeColorScheme,
        ThemeVariant? kdeVariant)
    {
        if (portalColorScheme is { } portalScheme)
        {
            return portalScheme == 1 ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        if (TryParseGnomeColorScheme(gnomeColorScheme, out var gnomeVariant))
        {
            return gnomeVariant;
        }

        return kdeVariant ?? ThemeVariant.Light;
    }

    /// <summary>Resolves system sans-serif UI font family independently from monospace.</summary>
    public static string ResolveUiFontFamily(
        string? gnomeFontName,
        string? kdeUiFont,
        string? fontConfigSans)
    {
        if (TryParseGnomeFontName(gnomeFontName, out var gnomeFamily))
        {
            return $"{gnomeFamily}, {DefaultUiFontFamily}";
        }

        if (!string.IsNullOrWhiteSpace(kdeUiFont))
        {
            return $"{kdeUiFont.Trim()}, {DefaultUiFontFamily}";
        }

        if (!string.IsNullOrWhiteSpace(fontConfigSans))
        {
            return $"{fontConfigSans.Trim()}, {DefaultUiFontFamily}";
        }

        return DefaultUiFontFamily;
    }

    /// <summary>Resolves monospace font chain reserved for IDs, pairing codes, and IP:port values.</summary>
    public static string ResolveMonospaceFontFamily(string? fontConfigMono)
    {
        if (string.IsNullOrWhiteSpace(fontConfigMono))
        {
            return DefaultMonospaceFontFamily;
        }

        var trimmed = fontConfigMono.Trim();
        return DefaultMonospaceFontFamily.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            ? DefaultMonospaceFontFamily
            : $"{trimmed}, {DefaultMonospaceFontFamily}";
    }

    /// <summary>Parses a Portal RGB normalized triple in [0, 1]. Rejects NaN, Inf, or out-of-range values.</summary>
    public static bool TryParsePortalAccent(double r, double g, double b, out Color color)
    {
        if (!IsUnitFinite(r) || !IsUnitFinite(g) || !IsUnitFinite(b))
        {
            color = default;
            return false;
        }

        color = Color.FromRgb(
            (byte)Math.Round(r * 255.0),
            (byte)Math.Round(g * 255.0),
            (byte)Math.Round(b * 255.0));
        return true;
    }

    /// <summary>Parses gsettings accent-color string (named token or hex).</summary>
    public static bool TryParseGnomeAccent(string? raw, out Color color)
    {
        var token = Unquote(raw);
        if (string.IsNullOrWhiteSpace(token))
        {
            color = default;
            return false;
        }

        if (GnomeAccentMap.TryGetValue(token, out color))
        {
            return true;
        }

        return TryParseHexColor(token, out color);
    }

    /// <summary>Parses gsettings color-scheme string ('prefer-dark', 'prefer-light').</summary>
    public static bool TryParseGnomeColorScheme(string? raw, out ThemeVariant variant)
    {
        var token = Unquote(raw);
        if (string.Equals(token, "prefer-dark", StringComparison.OrdinalIgnoreCase))
        {
            variant = ThemeVariant.Dark;
            return true;
        }

        if (string.Equals(token, "prefer-light", StringComparison.OrdinalIgnoreCase))
        {
            variant = ThemeVariant.Light;
            return true;
        }

        variant = ThemeVariant.Light;
        return false;
    }

    /// <summary>Extracts font family name from gsettings font-name (e.g. "'Cantarell 11'" -> "Cantarell").</summary>
    public static bool TryParseGnomeFontName(string? raw, out string family)
    {
        var token = Unquote(raw);
        if (string.IsNullOrWhiteSpace(token))
        {
            family = string.Empty;
            return false;
        }

        var lastSpace = token.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            var tail = token[(lastSpace + 1)..];
            if (double.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                token = token[..lastSpace].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            family = string.Empty;
            return false;
        }

        family = token;
        return true;
    }

    /// <summary>Parses KDE kdeglobals content for AccentColor, ColorScheme, and UI font.</summary>
    public static (Color? AccentColor, ThemeVariant? Variant, string? UiFontFamily) ParseKdeGlobals(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, null, null);
        }

        var section = string.Empty;
        Color? accent = null;
        ThemeVariant? variant = null;
        string? uiFont = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("AccentColor", StringComparison.OrdinalIgnoreCase) &&
                    TryParseRgbOrHex(value, out var parsedAccent))
                {
                    accent = parsedAccent;
                }
                else if (key.Equals("ColorScheme", StringComparison.OrdinalIgnoreCase) &&
                         value.Contains("Dark", StringComparison.OrdinalIgnoreCase))
                {
                    variant = ThemeVariant.Dark;
                }
                else if (key.Equals("font", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = value.IndexOf(',');
                    var fam = (comma > 0 ? value[..comma] : value).Trim();
                    if (!string.IsNullOrWhiteSpace(fam))
                    {
                        uiFont = fam;
                    }
                }
            }
            else if (section.Equals("Colors:Window", StringComparison.OrdinalIgnoreCase) &&
                     key.Equals("BackgroundNormal", StringComparison.OrdinalIgnoreCase) &&
                     variant is null &&
                     TryParseRgbOrHex(value, out var windowBg))
            {
                var luma = (0.2126 * windowBg.R + 0.7152 * windowBg.G + 0.0722 * windowBg.B) / 255.0;
                variant = luma < 0.45 ? ThemeVariant.Dark : ThemeVariant.Light;
            }
        }

        return (accent, variant, uiFont);
    }

    private static bool TryParseRgbOrHex(string value, out Color color)
    {
        if (TryParseHexColor(value, out color))
        {
            return true;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 &&
            byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            color = Color.FromRgb(r, g, b);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryParseHexColor(string value, out Color color)
    {
        var hex = value.Trim();
        if (hex.StartsWith('#') &&
            (hex.Length == 7 || hex.Length == 9) &&
            Color.TryParse(hex, out color))
        {
            color = Color.FromRgb(color.R, color.G, color.B);
            return true;
        }

        color = default;
        return false;
    }

    private static bool IsUnitFinite(double value) =>
        double.IsFinite(value) && value >= 0.0 && value <= 1.0;

    private static string Unquote(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().Trim('\'', '"');
}

/// <summary>Live Linux appearance source using XDG Portal Settings D-Bus, kdeglobals, gsettings, and fontconfig.</summary>
public sealed class DesktopLinuxAppearanceSource : ILinuxAppearanceSource
{
    private const string PortalBusName = "org.freedesktop.portal.Desktop";
    private const string PortalObjectPath = "/org/freedesktop/portal/desktop";
    private const string PortalSettingsInterface = "org.freedesktop.portal.Settings";
    private const string AppearanceNamespace = "org.freedesktop.appearance";

    private readonly Lock _gate = new();
    private DBusConnection? _dbus;
    private IDisposable? _signalSubscription;
    private FileSystemWatcher? _kdeWatcher;
    private uint? _cachedPortalScheme;
    private Color? _cachedPortalAccent;
    private string? _cachedFontSans;
    private string? _cachedFontMono;
    private LinuxAppearanceSnapshot? _lastSnapshot;
    private bool _disposed;

    public event Action<LinuxAppearanceSnapshot>? Changed;

    public async Task<LinuxAppearanceSnapshot> StartAsync(CancellationToken ct = default)
    {
        _cachedFontSans = await RunCommandTrimmedAsync("fc-match", ["-f", "%{family[0]}", "sans-serif"], ct);
        _cachedFontMono = await RunCommandTrimmedAsync("fc-match", ["-f", "%{family[0]}", "monospace"], ct);

        await ConnectPortalAsync(ct);
        StartKdeWatcher();

        var snapshot = await RefreshProbeAndBuildAsync(ct);
        lock (_gate)
        {
            _lastSnapshot = snapshot;
        }

        return snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _signalSubscription?.Dispose();
        _kdeWatcher?.Dispose();
        _dbus?.Dispose();
        await Task.CompletedTask;
    }

    private async Task ConnectPortalAsync(CancellationToken ct)
    {
        var address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        try
        {
            var connection = new DBusConnection(address);
            await connection.ConnectAsync().AsTask().WaitAsync(ct);

            var sub = await connection.WatchSignalAsync<(string Ns, string Key, VariantValue Value)>(
                sender: PortalBusName,
                path: PortalObjectPath,
                @interface: PortalSettingsInterface,
                signal: "SettingChanged",
                reader: static (Message msg, object? _) => ReadSettingChanged(msg),
                handler: static notification =>
                {
                    if (notification.HasValue && notification.State is DesktopLinuxAppearanceSource self)
                    {
                        self.HandlePortalSettingChanged(notification.Value.Ns, notification.Value.Key, notification.Value.Value);
                    }
                },
                flags: ObserverFlags.None,
                emitOnCapturedContext: false,
                state: this).AsTask().WaitAsync(ct);

            _dbus = connection;
            _signalSubscription = sub;

            _cachedPortalScheme = await ReadPortalColorSchemeAsync(connection, ct);
            _cachedPortalAccent = await ReadPortalAccentAsync(connection, ct);
        }
        catch
        {
            // Headless / no session D-Bus / portal absent: fall back to KDE/GNOME/default cleanly.
        }
    }

    private void HandlePortalSettingChanged(string ns, string key, VariantValue rawValue)
    {
        if (!string.Equals(ns, AppearanceNamespace, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(key, "color-scheme", StringComparison.Ordinal))
        {
            _cachedPortalScheme = TryExtractUInt(rawValue, out var scheme) ? scheme : null;
        }
        else if (string.Equals(key, "accent-color", StringComparison.Ordinal))
        {
            _cachedPortalAccent = TryExtractPortalColor(rawValue, out var accent) ? accent : null;
        }
        else
        {
            return;
        }

        _ = RefreshAndNotifyAsync();
    }

    private async Task RefreshAndNotifyAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            var next = await RefreshProbeAndBuildAsync(CancellationToken.None);
            bool changed;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                changed = _lastSnapshot is null || !Equals(_lastSnapshot, next);
                _lastSnapshot = next;
            }

            if (changed)
            {
                Changed?.Invoke(next);
            }
        }
        catch
        {
            // Ignore transient refresh failures during shutdown or file save races.
        }
    }

    private async Task<LinuxAppearanceSnapshot> RefreshProbeAndBuildAsync(CancellationToken ct)
    {
        var kdeContent = await ReadKdeGlobalsContentAsync(ct);
        var gnomeScheme = await RunCommandTrimmedAsync("gsettings", ["get", "org.gnome.desktop.interface", "color-scheme"], ct);
        var gnomeAccent = await RunCommandTrimmedAsync("gsettings", ["get", "org.gnome.desktop.interface", "accent-color"], ct);
        var gnomeFont = await RunCommandTrimmedAsync("gsettings", ["get", "org.gnome.desktop.interface", "font-name"], ct);

        var probe = new AppearanceProbeData(
            PortalColorScheme: _cachedPortalScheme,
            PortalAccentColor: _cachedPortalAccent,
            GnomeColorScheme: gnomeScheme,
            GnomeAccentColor: gnomeAccent,
            GnomeFontName: gnomeFont,
            KdeGlobalsContent: kdeContent,
            FontConfigSansFamily: _cachedFontSans,
            FontConfigMonoFamily: _cachedFontMono);

        return AppearanceResolver.Resolve(probe);
    }

    private void StartKdeWatcher()
    {
        var configDir = GetConfigDirectory();
        if (string.IsNullOrEmpty(configDir) || !Directory.Exists(configDir))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(configDir, "kdeglobals")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };

            watcher.Changed += (_, _) => _ = RefreshAndNotifyAsync();
            watcher.Created += (_, _) => _ = RefreshAndNotifyAsync();
            watcher.Renamed += (_, _) => _ = RefreshAndNotifyAsync();
            watcher.Deleted += (_, _) => _ = RefreshAndNotifyAsync();
            _kdeWatcher = watcher;
        }
        catch
        {
            // Watchers may be unavailable in restricted sandboxes.
        }
    }

    private static async Task<string?> ReadKdeGlobalsContentAsync(CancellationToken ct)
    {
        var configDir = GetConfigDirectory();
        if (string.IsNullOrEmpty(configDir))
        {
            return null;
        }

        var path = Path.Combine(configDir, "kdeglobals");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(path, ct);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetConfigDirectory()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return xdg;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, ".config");
    }

    private static async Task<uint?> ReadPortalColorSchemeAsync(DBusConnection connection, CancellationToken ct)
    {
        try
        {
            var value = await CallPortalReadOneAsync(connection, AppearanceNamespace, "color-scheme", ct);
            return TryExtractUInt(value, out var scheme) ? scheme : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Color?> ReadPortalAccentAsync(DBusConnection connection, CancellationToken ct)
    {
        try
        {
            var value = await CallPortalReadOneAsync(connection, AppearanceNamespace, "accent-color", ct);
            return TryExtractPortalColor(value, out var color) ? color : null;
        }
        catch
        {
            return null;
        }
    }

    private static Task<VariantValue> CallPortalReadOneAsync(
        DBusConnection connection,
        string ns,
        string key,
        CancellationToken ct)
    {
        var message = BuildReadOneMessage(connection, ns, key);
        return connection.CallMethodAsync(
            message,
            static (Message reply, object? _) => reply.GetBodyReader().ReadVariantValue(),
            null).WaitAsync(ct);
    }

    private static MessageBuffer BuildReadOneMessage(DBusConnection connection, string ns, string key)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            PortalBusName,
            PortalObjectPath,
            PortalSettingsInterface,
            "ReadOne",
            "ss",
            MessageFlags.None);
        writer.WriteString(ns);
        writer.WriteString(key);
        return writer.CreateMessage();
    }

    private static (string Ns, string Key, VariantValue Value) ReadSettingChanged(Message message)
    {
        var reader = message.GetBodyReader();
        var ns = reader.ReadString();
        var key = reader.ReadString();
        var val = reader.ReadVariantValue();
        return (ns, key, val);
    }

    private static bool TryExtractUInt(VariantValue raw, out uint value)
    {
        var inner = UnwrapVariants(raw);
        if (inner.Type == VariantValueType.UInt32)
        {
            value = inner.GetUInt32();
            return true;
        }

        if (inner.Type == VariantValueType.Int32)
        {
            var signed = inner.GetInt32();
            if (signed >= 0)
            {
                value = (uint)signed;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryExtractPortalColor(VariantValue raw, out Color color)
    {
        var inner = UnwrapVariants(raw);
        if (inner.Type == VariantValueType.Struct && inner.Count >= 3)
        {
            var rVal = UnwrapVariants(inner.GetItem(0));
            var gVal = UnwrapVariants(inner.GetItem(1));
            var bVal = UnwrapVariants(inner.GetItem(2));
            if (rVal.Type == VariantValueType.Double &&
                gVal.Type == VariantValueType.Double &&
                bVal.Type == VariantValueType.Double)
            {
                return AppearanceResolver.TryParsePortalAccent(
                    rVal.GetDouble(),
                    gVal.GetDouble(),
                    bVal.GetDouble(),
                    out color);
            }
        }

        color = default;
        return false;
    }

    private static VariantValue UnwrapVariants(VariantValue value)
    {
        while (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue();
        }

        return value;
    }

    private static async Task<string?> RunCommandTrimmedAsync(string fileName, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1200));

            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            if (process.ExitCode != 0)
            {
                return null;
            }

            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Coordinates MD3E palette application onto an Avalonia Application or ResourceDictionary.</summary>
public sealed class LinuxAppearanceCoordinator : IAsyncDisposable
{
    private readonly ILinuxAppearanceSource _source;
    private readonly Action<ThemeVariant> _setThemeVariant;
    private readonly IResourceDictionary _resources;
    private readonly MaterialTheme? _materialTheme;
    private readonly Action<Action> _dispatchToUi;
    private LinuxAppearanceSnapshot _lastDesktopSnapshot;
    private LinuxAppearanceSnapshot _current;
    private AppearanceModeOverride _modeOverride = AppearanceModeOverride.FollowSystem;
    private AccentSourceOverride _accentOverride = AccentSourceOverride.FollowSystem;

    public LinuxAppearanceCoordinator(
        ILinuxAppearanceSource source,
        Action<ThemeVariant> setThemeVariant,
        IResourceDictionary resources,
        Action<Action>? dispatchToUi = null,
        MaterialTheme? materialTheme = null)
    {
        _source = source;
        _setThemeVariant = setThemeVariant;
        _resources = resources;
        _materialTheme = materialTheme;
        _dispatchToUi = dispatchToUi ?? (static work => work());
        _lastDesktopSnapshot = new LinuxAppearanceSnapshot(
            ThemeVariant.Light,
            HctColorScheme.DefaultBrandSeed,
            AppearanceResolver.DefaultUiFontFamily,
            AppearanceResolver.DefaultMonospaceFontFamily);
        _current = _lastDesktopSnapshot;
    }

    public LinuxAppearanceSnapshot Current => _current;
    public event Action<LinuxAppearanceSnapshot>? SnapshotApplied;

    public void SetOverrides(AppearanceModeOverride? modeOverride, AccentSourceOverride? accentOverride)
    {
        _modeOverride = modeOverride ?? AppearanceModeOverride.FollowSystem;
        _accentOverride = accentOverride ?? AccentSourceOverride.FollowSystem;
        _dispatchToUi(() => ApplyEffectiveSnapshot(_lastDesktopSnapshot));
    }

    public async Task<LinuxAppearanceSnapshot> StartAsync(CancellationToken ct = default)
    {
        _source.Changed += OnSourceChanged;
        var initial = await _source.StartAsync(ct);
        ApplySnapshot(initial);
        return _current;
    }

    private void OnSourceChanged(LinuxAppearanceSnapshot snapshot)
    {
        _dispatchToUi(() => ApplySnapshot(snapshot));
    }

    public void ApplySnapshot(LinuxAppearanceSnapshot snapshot)
    {
        _lastDesktopSnapshot = snapshot;
        ApplyEffectiveSnapshot(snapshot);
    }

    private void ApplyEffectiveSnapshot(LinuxAppearanceSnapshot desktopSnapshot)
    {
        var effectiveVariant = _modeOverride switch
        {
            AppearanceModeOverride.Light => ThemeVariant.Light,
            AppearanceModeOverride.Dark => ThemeVariant.Dark,
            _ => desktopSnapshot.Variant,
        };
        var effectiveAccent = _accentOverride switch
        {
            AccentSourceOverride.BrandBlue
            or AccentSourceOverride.ForestTeal
            or AccentSourceOverride.SunsetTerracotta
            or AccentSourceOverride.VioletIndigo => AppearanceResolver.GetPresetAccent(_accentOverride),
            _ => desktopSnapshot.AccentSeed,
        };

        var effectiveSnapshot = desktopSnapshot with
        {
            Variant = effectiveVariant,
            AccentSeed = effectiveAccent,
        };
        _current = effectiveSnapshot;
        _setThemeVariant(effectiveSnapshot.Variant);
        var lightPalette = HctColorScheme.Generate(effectiveSnapshot.AccentSeed, ThemeVariant.Light);
        var darkPalette = HctColorScheme.Generate(effectiveSnapshot.AccentSeed, ThemeVariant.Dark);
        Md3eThemeAdapter.ApplyToResourceDictionary(_resources, _materialTheme, effectiveSnapshot, lightPalette, darkPalette);
        SnapshotApplied?.Invoke(effectiveSnapshot);
    }

    public ValueTask DisposeAsync()
    {
        _source.Changed -= OnSourceChanged;
        return _source.DisposeAsync();
    }
}
