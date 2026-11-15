using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tmds.DBus.Protocol;

namespace ContextAll.App.Platform;

/// <summary>Resolved desktop cursor theme, size, and icon search path for Wayland/X11 toolkits.</summary>
public sealed record CursorSettings(string Theme, int Size, string CursorPath);

/// <summary>Collected environment, XDG Portal, GTK, KDE, gsettings, and filesystem inputs for cursor resolution.</summary>
public sealed record CursorProbeInputs(
    string? EnvXcursorTheme = null,
    string? EnvXcursorSize = null,
    string? EnvXcursorPath = null,
    string? XdgDataDirs = null,
    string? CurrentDesktop = null,
    string? PortalCursorTheme = null,
    int? PortalCursorSize = null,
    string? KdeInputRcContent = null,
    string? Gtk3SettingsIniContent = null,
    string? Gtk4SettingsIniContent = null,
    string? DefaultIndexThemeContent = null,
    string? GsettingsCursorTheme = null,
    string? GsettingsCursorSize = null,
    string? HomeDirectory = null,
    string? UserName = null,
    IReadOnlyList<string>? CandidateIconDirectories = null,
    IReadOnlyList<string>? ExistingCursorThemes = null,
    string? RuntimeAliasIconsDirectory = null);

/// <summary>
/// Resolves Linux desktop cursor theme and size before Avalonia Wayland/X11 initialization and
/// prepares a runtime <c>default</c> cursor theme alias on <c>XCURSOR_PATH</c> so
/// <c>Avalonia.Wayland</c> (<c>wl_cursor_theme_load("default", ...)</c>) loads the active desktop theme.
/// </summary>
public static class LinuxCursorEnvironment
{
    public const string DefaultThemeFallback = "Adwaita";
    public const int DefaultSizeFallback = 24;
    private const int MinCursorSize = 12;
    private const int MaxCursorSize = 128;

    private const string PortalBusName = "org.freedesktop.portal.Desktop";
    private const string PortalObjectPath = "/org/freedesktop/portal/desktop";
    private const string PortalSettingsInterface = "org.freedesktop.portal.Settings";
    private const string GnomeInterfaceNamespace = "org.gnome.desktop.interface";

    private static readonly string[] PreferredThemeOrder =
    [
        "Adwaita",
        "breeze_cursors",
        "Bibata-Modern-Classic",
        "DMZ-White",
    ];

    /// <summary>Pure resolver computing cursor theme, size, and deduplicated <c>XCURSOR_PATH</c>.</summary>
    public static CursorSettings Resolve(CursorProbeInputs inputs)
    {
        var preferKde = IsKdeDesktop(inputs.CurrentDesktop);

        var configTheme = ReadConfigTheme(inputs, preferKde);
        var theme = FirstValidTheme(
            NormalizeThemeName(inputs.EnvXcursorTheme),
            NormalizeThemeName(inputs.PortalCursorTheme),
            configTheme,
            NormalizeThemeName(inputs.GsettingsCursorTheme),
            PickInstalledTheme(inputs.ExistingCursorThemes))
            ?? DefaultThemeFallback;

        var configSize = ReadConfigSize(inputs, preferKde);
        var size = FirstValidSize(
            TryParseSize(inputs.EnvXcursorSize),
            ClampOptionalSize(inputs.PortalCursorSize),
            configSize,
            TryParseSize(inputs.GsettingsCursorSize))
            ?? DefaultSizeFallback;

        var cursorPath = BuildCursorPath(inputs);
        return new CursorSettings(theme, size, cursorPath);
    }

    /// <summary>
    /// Builds an ordered list of candidate cursor icon search directories across user, XDG,
    /// NixOS profile/system, and FHS paths.
    /// </summary>
    public static IReadOnlyList<string> BuildCandidateIconDirectories(
        string? homeDirectory,
        string? xdgDataDirs,
        string? userName)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        static void AddCandidate(List<string> list, HashSet<string> dedupe, string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            var trimmed = dir.Trim().TrimEnd('/');
            if (trimmed.Length > 0 && dedupe.Add(trimmed))
            {
                list.Add(trimmed);
            }
        }

        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            var home = homeDirectory.TrimEnd('/');
            AddCandidate(result, seen, $"{home}/.local/share/icons");
            AddCandidate(result, seen, $"{home}/.icons");
        }

        if (!string.IsNullOrWhiteSpace(xdgDataDirs))
        {
            foreach (var rawDir in xdgDataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddCandidate(result, seen, $"{rawDir.TrimEnd('/')}/icons");
            }
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            AddCandidate(result, seen, $"/etc/profiles/per-user/{userName.Trim()}/share/icons");
        }

        AddCandidate(result, seen, "/run/current-system/sw/share/icons");
        AddCandidate(result, seen, "/usr/share/icons");
        AddCandidate(result, seen, "/usr/share/pixmaps");
        return result;
    }

    /// <summary>
    /// Creates <c>&lt;runtimeBaseDir&gt;/contextall-xcursor/default</c> with <c>index.theme</c> inheriting
    /// <paramref name="resolvedTheme"/> and a <c>cursors</c> symlink pointing to the resolved theme's
    /// <c>cursors</c> directory if located in <paramref name="iconSearchDirectories"/>.
    /// </summary>
    public static string? PrepareDefaultThemeAlias(
        string runtimeBaseDir,
        string resolvedTheme,
        IEnumerable<string> iconSearchDirectories)
    {
        if (string.IsNullOrWhiteSpace(runtimeBaseDir))
        {
            return null;
        }

        try
        {
            var theme = NormalizeThemeName(resolvedTheme) ?? DefaultThemeFallback;
            var aliasRoot = Path.Combine(runtimeBaseDir, "contextall-xcursor");
            var defaultDir = Path.Combine(aliasRoot, "default");
            Directory.CreateDirectory(aliasRoot);
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    File.SetUnixFileMode(
                        aliasRoot,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch
                {
                    // Best-effort restrictive permissions on runtime directory.
                }
            }

            Directory.CreateDirectory(defaultDir);

            var indexPath = Path.Combine(defaultDir, "index.theme");
            var indexContent = $"[Icon Theme]\nName=Default\nInherits={theme}\n";
            File.WriteAllText(indexPath, indexContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (!string.Equals(theme, "default", StringComparison.Ordinal))
            {
                var sourceCursorsDir = FindCursorsDirectory(aliasRoot, theme, iconSearchDirectories);
                if (sourceCursorsDir is not null)
                {
                    var linkPath = Path.Combine(defaultDir, "cursors");
                    RemoveExistingEntry(linkPath);
                    Directory.CreateSymbolicLink(linkPath, sourceCursorsDir);
                }
            }

            return aliasRoot;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Probes desktop cursor settings (env, XDG Portal, GTK/KDE config, gsettings, installed themes)
    /// and applies <c>XCURSOR_THEME</c>, <c>XCURSOR_SIZE</c>, and <c>XCURSOR_PATH</c> cleanly.
    /// </summary>
    public static Task ApplyIfUnsetAsync(CancellationToken ct = default) => ApplyAsync(ct);

    /// <summary>
    /// Probes desktop cursor settings (env, XDG Portal, GTK/KDE config, gsettings, installed themes)
    /// and exports <c>XCURSOR_THEME</c>, <c>XCURSOR_SIZE</c>, and <c>XCURSOR_PATH</c> (including runtime <c>default</c> alias).
    /// </summary>
    public static async Task ApplyAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var envTheme = Environment.GetEnvironmentVariable("XCURSOR_THEME");
            var envSize = Environment.GetEnvironmentVariable("XCURSOR_SIZE");
            var envPath = Environment.GetEnvironmentVariable("XCURSOR_PATH");
            var xdgDataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            var currentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userName = Environment.GetEnvironmentVariable("USER");
            if (string.IsNullOrWhiteSpace(userName))
            {
                userName = Environment.UserName;
            }

            var candidateDirs = BuildCandidateIconDirectories(homeDir, xdgDataDirs, userName);
            var searchDirs = BuildSearchOrderForDiscovery(envPath, candidateDirs);
            var existingIconDirs = searchDirs.Where(Directory.Exists).ToArray();

            string? portalTheme = null;
            int? portalSize = null;
            string? kdeInputRc = null;
            string? gtk3Ini = null;
            string? gtk4Ini = null;
            string? defaultIndexTheme = null;
            string? gsettingsTheme = null;
            string? gsettingsSize = null;

            if (string.IsNullOrWhiteSpace(envTheme) || string.IsNullOrWhiteSpace(envSize))
            {
                (portalTheme, portalSize) = await ReadPortalCursorSettingsAsync(ct).ConfigureAwait(false);

                var configDir = GetConfigDirectory(homeDir);
                if (!string.IsNullOrWhiteSpace(configDir))
                {
                    kdeInputRc = await TryReadFileAsync(Path.Combine(configDir, "kcminputrc"), ct).ConfigureAwait(false);
                    gtk3Ini = await TryReadFileAsync(Path.Combine(configDir, "gtk-3.0", "settings.ini"), ct).ConfigureAwait(false);
                    gtk4Ini = await TryReadFileAsync(Path.Combine(configDir, "gtk-4.0", "settings.ini"), ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(homeDir))
                {
                    defaultIndexTheme = await TryReadFileAsync(
                        Path.Combine(homeDir, ".icons", "default", "index.theme"),
                        ct).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(portalTheme))
                {
                    gsettingsTheme = await RunCommandTrimmedAsync(
                        "gsettings",
                        ["get", GnomeInterfaceNamespace, "cursor-theme"],
                        ct).ConfigureAwait(false);
                }

                if (portalSize is null)
                {
                    gsettingsSize = await RunCommandTrimmedAsync(
                        "gsettings",
                        ["get", GnomeInterfaceNamespace, "cursor-size"],
                        ct).ConfigureAwait(false);
                }
            }

            var installedThemes = DiscoverInstalledCursorThemes(existingIconDirs);

            var preliminary = Resolve(new CursorProbeInputs(
                EnvXcursorTheme: envTheme,
                EnvXcursorSize: envSize,
                EnvXcursorPath: envPath,
                XdgDataDirs: xdgDataDirs,
                CurrentDesktop: currentDesktop,
                PortalCursorTheme: portalTheme,
                PortalCursorSize: portalSize,
                KdeInputRcContent: kdeInputRc,
                Gtk3SettingsIniContent: gtk3Ini,
                Gtk4SettingsIniContent: gtk4Ini,
                DefaultIndexThemeContent: defaultIndexTheme,
                GsettingsCursorTheme: gsettingsTheme,
                GsettingsCursorSize: gsettingsSize,
                HomeDirectory: homeDir,
                UserName: userName,
                CandidateIconDirectories: existingIconDirs,
                ExistingCursorThemes: installedThemes));

            var runtimeBase = !string.IsNullOrWhiteSpace(runtimeDir) && Directory.Exists(runtimeDir)
                ? runtimeDir
                : Path.Combine(Path.GetTempPath(), $"contextall-runtime-{userName}");

            var aliasRoot = PrepareDefaultThemeAlias(runtimeBase, preliminary.Theme, existingIconDirs);

            var final = Resolve(new CursorProbeInputs(
                EnvXcursorTheme: preliminary.Theme,
                EnvXcursorSize: preliminary.Size.ToString(CultureInfo.InvariantCulture),
                EnvXcursorPath: envPath,
                XdgDataDirs: xdgDataDirs,
                CurrentDesktop: currentDesktop,
                HomeDirectory: homeDir,
                UserName: userName,
                CandidateIconDirectories: existingIconDirs,
                ExistingCursorThemes: installedThemes,
                RuntimeAliasIconsDirectory: aliasRoot));

            Environment.SetEnvironmentVariable("XCURSOR_THEME", final.Theme);
            Environment.SetEnvironmentVariable("XCURSOR_SIZE", final.Size.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(final.CursorPath))
            {
                Environment.SetEnvironmentVariable("XCURSOR_PATH", final.CursorPath);
            }
        }
        catch
        {
            // Never block desktop startup if cursor probing fails in headless/restricted environments.
        }
    }

    private static string BuildCursorPath(CursorProbeInputs inputs)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        static void AddPath(List<string> list, HashSet<string> dedupe, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > 0 && dedupe.Add(trimmed))
            {
                list.Add(trimmed);
            }
        }

        AddPath(parts, seen, inputs.RuntimeAliasIconsDirectory);

        if (!string.IsNullOrWhiteSpace(inputs.EnvXcursorPath))
        {
            foreach (var entry in inputs.EnvXcursorPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddPath(parts, seen, entry);
            }
        }

        var candidates = inputs.CandidateIconDirectories
            ?? BuildCandidateIconDirectories(inputs.HomeDirectory, inputs.XdgDataDirs, inputs.UserName);
        foreach (var candidate in candidates)
        {
            AddPath(parts, seen, candidate);
        }

        return string.Join(':', parts);
    }

    private static string? FindCursorsDirectory(
        string aliasRoot,
        string theme,
        IEnumerable<string> iconSearchDirectories)
    {
        var fullAliasRoot = Path.GetFullPath(aliasRoot).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var dir in iconSearchDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                var fullDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(fullDir, fullAliasRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                var cursorsDir = Path.Combine(fullDir, theme, "cursors");
                if (Directory.Exists(cursorsDir))
                {
                    return cursorsDir;
                }
            }
            catch
            {
                // Ignore malformed paths while scanning icon roots.
            }
        }

        return null;
    }

    private static void RemoveExistingEntry(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        if (dirInfo.LinkTarget is not null)
        {
            dirInfo.Delete();
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static IReadOnlyList<string> BuildSearchOrderForDiscovery(
        string? envXcursorPath,
        IReadOnlyList<string> candidates)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(envXcursorPath))
        {
            foreach (var entry in envXcursorPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(entry))
                {
                    list.Add(entry);
                }
            }
        }

        foreach (var candidate in candidates)
        {
            if (seen.Add(candidate))
            {
                list.Add(candidate);
            }
        }

        return list;
    }

    private static IReadOnlyList<string> DiscoverInstalledCursorThemes(IEnumerable<string> iconDirs)
    {
        var themes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var iconDir in iconDirs)
        {
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(iconDir))
                {
                    var name = Path.GetFileName(subDir);
                    if (string.IsNullOrWhiteSpace(name) ||
                        string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "hicolor", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (Directory.Exists(Path.Combine(subDir, "cursors")) && seen.Add(name))
                    {
                        themes.Add(name);
                    }
                }
            }
            catch
            {
                // Ignore unreadable icon directories.
            }
        }

        return themes;
    }

    private static bool IsKdeDesktop(string? currentDesktop) =>
        !string.IsNullOrWhiteSpace(currentDesktop) &&
        currentDesktop.Contains("KDE", StringComparison.OrdinalIgnoreCase);

    private static string? ReadConfigTheme(CursorProbeInputs inputs, bool preferKde)
    {
        var kdeTheme = NormalizeThemeName(ReadIniValue(inputs.KdeInputRcContent, "Mouse", "cursorTheme"));
        var gtk4Theme = NormalizeThemeName(ReadIniValue(inputs.Gtk4SettingsIniContent, "Settings", "gtk-cursor-theme-name"));
        var gtk3Theme = NormalizeThemeName(ReadIniValue(inputs.Gtk3SettingsIniContent, "Settings", "gtk-cursor-theme-name"));
        var defaultTheme = NormalizeThemeName(ReadIniValue(inputs.DefaultIndexThemeContent, "Icon Theme", "Inherits"));

        return preferKde
            ? FirstValidTheme(kdeTheme, gtk4Theme, gtk3Theme, defaultTheme)
            : FirstValidTheme(gtk4Theme, gtk3Theme, defaultTheme, kdeTheme);
    }

    private static int? ReadConfigSize(CursorProbeInputs inputs, bool preferKde)
    {
        var kdeSize = TryParseSize(ReadIniValue(inputs.KdeInputRcContent, "Mouse", "cursorSize"));
        var gtk4Size = TryParseSize(ReadIniValue(inputs.Gtk4SettingsIniContent, "Settings", "gtk-cursor-theme-size"));
        var gtk3Size = TryParseSize(ReadIniValue(inputs.Gtk3SettingsIniContent, "Settings", "gtk-cursor-theme-size"));

        return preferKde
            ? FirstValidSize(kdeSize, gtk4Size, gtk3Size)
            : FirstValidSize(gtk4Size, gtk3Size, kdeSize);
    }

    private static string? PickInstalledTheme(IReadOnlyList<string>? existingThemes)
    {
        if (existingThemes is null || existingThemes.Count == 0)
        {
            return null;
        }

        foreach (var preferred in PreferredThemeOrder)
        {
            foreach (var installed in existingThemes)
            {
                var normalized = NormalizeThemeName(installed);
                if (normalized is not null && string.Equals(normalized, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    return normalized;
                }
            }
        }

        foreach (var installed in existingThemes)
        {
            var normalized = NormalizeThemeName(installed);
            if (normalized is not null &&
                !string.Equals(normalized, "hicolor", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? ReadIniValue(string? iniContent, string targetSection, string targetKey)
    {
        if (string.IsNullOrWhiteSpace(iniContent))
        {
            return null;
        }

        var inSection = false;
        foreach (var rawLine in iniContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                inSection = string.Equals(section, targetSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0)
            {
                continue;
            }

            var key = line[..eqIdx].Trim();
            if (string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase))
            {
                return line[(eqIdx + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? FirstValidTheme(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int? FirstValidSize(params int?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate is { } valid)
            {
                return valid;
            }
        }

        return null;
    }

    private static string? NormalizeThemeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Trim('\'', '"').Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }

    private static int? TryParseSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Trim('\'', '"');
        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            return null;
        }

        return Math.Clamp(parsed, MinCursorSize, MaxCursorSize);
    }

    private static int? ClampOptionalSize(int? raw) =>
        raw is { } value && value > 0 ? Math.Clamp(value, MinCursorSize, MaxCursorSize) : null;

    private static string? GetConfigDirectory(string? homeDirectory)
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return xdg;
        }

        return string.IsNullOrWhiteSpace(homeDirectory) ? null : Path.Combine(homeDirectory, ".config");
    }

    private static async Task<(string? Theme, int? Size)> ReadPortalCursorSettingsAsync(CancellationToken ct)
    {
        var address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
        {
            return (null, null);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(450));

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().AsTask().WaitAsync(timeoutCts.Token).ConfigureAwait(false);

            string? theme = null;
            int? size = null;

            try
            {
                var themeVal = await CallPortalReadOneAsync(connection, GnomeInterfaceNamespace, "cursor-theme", timeoutCts.Token).ConfigureAwait(false);
                var unwrapped = UnwrapVariants(themeVal);
                if (unwrapped.Type == VariantValueType.String)
                {
                    theme = NormalizeThemeName(unwrapped.GetString());
                }
            }
            catch
            {
                // Ignore individual portal key read failures.
            }

            try
            {
                var sizeVal = await CallPortalReadOneAsync(connection, GnomeInterfaceNamespace, "cursor-size", timeoutCts.Token).ConfigureAwait(false);
                var unwrapped = UnwrapVariants(sizeVal);
                if (unwrapped.Type == VariantValueType.Int32)
                {
                    size = ClampOptionalSize(unwrapped.GetInt32());
                }
                else if (unwrapped.Type == VariantValueType.UInt32)
                {
                    var u = unwrapped.GetUInt32();
                    if (u <= MaxCursorSize)
                    {
                        size = ClampOptionalSize((int)u);
                    }
                    else if (u > 0)
                    {
                        size = MaxCursorSize;
                    }
                }
            }
            catch
            {
                // Ignore individual portal key read failures.
            }

            return (theme, size);
        }
        catch
        {
            return (null, null);
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

    private static VariantValue UnwrapVariants(VariantValue value)
    {
        while (value.Type == VariantValueType.Variant)
        {
            value = value.GetVariantValue();
        }

        return value;
    }

    private static async Task<string?> TryReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> RunCommandTrimmedAsync(string fileName, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
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

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(450));

            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
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
