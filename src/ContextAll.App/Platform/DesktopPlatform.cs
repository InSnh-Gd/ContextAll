using ContextAll.App.Theming;

namespace ContextAll.App.Platform;

/// <summary>OS-specific desktop host services injected by App.Desktop / App.Windows.</summary>
public interface IDesktopPlatform
{
    bool PreferNativeTitleBar { get; }

    string DisplayServerLabel { get; }

    string ResolvePreferencesPath();

    IDaemonController CreateDaemonController();

    IStatusNotifierProbe CreateTrayProbe();

    ILinuxAppearanceSource CreateAppearanceSource();
}

public static class DesktopPlatform
{
    public static IDesktopPlatform Current { get; set; } = LinuxDesktopPlatform.Instance;
}

public sealed class LinuxDesktopPlatform : IDesktopPlatform
{
    public static LinuxDesktopPlatform Instance { get; } = new();

    public bool PreferNativeTitleBar => false;

    public string DisplayServerLabel => ThisDeviceDisplayLabel();

    public string ResolvePreferencesPath() =>
        ShellPreferencesStore.PreferencesPath(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    public IDaemonController CreateDaemonController() => new LinuxDaemonController();

    public IStatusNotifierProbe CreateTrayProbe() => new DbusStatusNotifierProbe();

    public ILinuxAppearanceSource CreateAppearanceSource() => new DesktopLinuxAppearanceSource();

    private static string ThisDeviceDisplayLabel() =>
        DisplayServerLabels.Resolve(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            isWindows: false);
}

public sealed class WindowsDesktopPlatform : IDesktopPlatform
{
    public static WindowsDesktopPlatform Instance { get; } = new();

    public bool PreferNativeTitleBar => true;

    public string DisplayServerLabel => "Win32";

    public string ResolvePreferencesPath() =>
        ShellPreferencesStore.WindowsPreferencesPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string ResolvePreferencesPath(string localAppData) =>
        ShellPreferencesStore.WindowsPreferencesPath(localAppData);

    public IDaemonController CreateDaemonController() => new WindowsDaemonController();

    public IStatusNotifierProbe CreateTrayProbe() => new AlwaysAvailableStatusNotifierProbe();

    public ILinuxAppearanceSource CreateAppearanceSource() => new WindowsAppearanceSource();
}
