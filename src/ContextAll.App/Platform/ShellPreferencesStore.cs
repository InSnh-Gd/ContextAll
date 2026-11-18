using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextAll.App.Platform;

[JsonConverter(typeof(JsonStringEnumConverter<AppearanceModeOverride>))]
public enum AppearanceModeOverride
{
    FollowSystem = 0,
    Light = 1,
    Dark = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<AccentSourceOverride>))]
public enum AccentSourceOverride
{
    FollowSystem = 0,
    BrandBlue = 1,
    ForestTeal = 2,
    SunsetTerracotta = 3,
    VioletIndigo = 4,
}

public sealed record ShellPreferences(
    bool TrayEnabled = false,
    bool UseNativeTitleBar = false,
    double WindowWidth = 980,
    double WindowHeight = 680,
    bool GuideSeen = false,
    AppearanceModeOverride? AppearanceModeOverride = null,
    AccentSourceOverride? AccentSourceOverride = null)
{
    [JsonIgnore]
    public AppearanceModeOverride EffectiveAppearanceMode =>
        AppearanceModeOverride ?? Platform.AppearanceModeOverride.FollowSystem;

    [JsonIgnore]
    public AccentSourceOverride EffectiveAccentSource =>
        AccentSourceOverride ?? Platform.AccentSourceOverride.FollowSystem;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ShellPreferences))]
internal partial class ShellJsonContext : JsonSerializerContext;

public static class ShellPreferencesStore
{
    public static string WindowsPreferencesPath(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        return Path.Combine(localAppData, "ContextAll", "shell.json");
    }

    public static string ForThisPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsPreferencesPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        return PreferencesPath(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
    }

    public static string PreferencesPath(string? configHome, string? userHome = null)
    {
        if (!string.IsNullOrWhiteSpace(configHome))
        {
            return Path.Combine(configHome, "contextall", "shell.json");
        }

        var home = !string.IsNullOrWhiteSpace(userHome)
            ? userHome
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.GetTempPath();
        }

        return Path.Combine(home, ".config", "contextall", "shell.json");
    }

    public static ShellPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ShellPreferences();
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize(json, ShellJsonContext.Default.ShellPreferences);
            return Normalize(parsed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ShellPreferences();
        }
    }

    public static void Save(string path, ShellPreferences preferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Normalize(preferences);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var tempPath = Path.Combine(
            directory ?? Path.GetTempPath(),
            $".shell.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(normalized, ShellJsonContext.Default.ShellPreferences);
        File.WriteAllText(tempPath, json);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    static ShellPreferences Normalize(ShellPreferences? raw)
    {
        if (raw is null)
        {
            return new ShellPreferences();
        }

        var width = double.IsFinite(raw.WindowWidth) && raw.WindowWidth >= ShellLayout.MinimumWidth
            ? raw.WindowWidth
            : 980;
        var height = double.IsFinite(raw.WindowHeight) && raw.WindowHeight >= ShellLayout.MinimumHeight
            ? raw.WindowHeight
            : 680;

        AppearanceModeOverride? mode = raw.AppearanceModeOverride.HasValue && Enum.IsDefined(raw.AppearanceModeOverride.Value)
            ? raw.AppearanceModeOverride
            : null;
        AccentSourceOverride? accent = raw.AccentSourceOverride.HasValue && Enum.IsDefined(raw.AccentSourceOverride.Value)
            ? raw.AccentSourceOverride
            : null;

        return raw with
        {
            WindowWidth = width,
            WindowHeight = height,
            AppearanceModeOverride = mode,
            AccentSourceOverride = accent,
        };
    }
}
