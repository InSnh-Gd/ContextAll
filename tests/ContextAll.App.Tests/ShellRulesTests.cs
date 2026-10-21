using Avalonia.Styling;
using ContextAll.App;

namespace ContextAll.App.Tests;

public sealed class ShellRulesTests
{
    [Fact]
    public void A_narrow_width_shows_one_column_and_a_wide_width_shows_both()
    {
        Assert.Equal(480, ShellLayout.MinimumWidth);
        Assert.Equal(640, ShellLayout.MinimumHeight);
        Assert.Equal(840, ShellLayout.TwoColumnWidth);
        Assert.Equal((true, false), ShellLayout.Columns(839, false));
        Assert.Equal((false, true), ShellLayout.Columns(839, true));
        Assert.Equal((true, true), ShellLayout.Columns(840, false));
        Assert.Equal((true, true), ShellLayout.Columns(1200, true));
    }

    [Fact]
    public void Portal_color_scheme_maps_dark_and_leaves_accent_unread()
    {
        Assert.Equal(ThemeVariant.Dark, PortalColorScheme.Variant(1));
        Assert.Equal(ThemeVariant.Light, PortalColorScheme.Variant(0));
        Assert.Equal(ThemeVariant.Light, PortalColorScheme.Variant(2));
        Assert.Equal(ThemeVariant.Light, PortalColorScheme.Variant(null));
    }

    [Fact]
    public void The_linux_shell_requires_a_wayland_display()
    {
        Assert.False(WaylandSession.IsPresent(null));
        Assert.False(WaylandSession.IsPresent(""));
        Assert.True(WaylandSession.IsPresent("wayland-0"));
        Assert.Contains("Wayland", WaylandSession.RequiredMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_linux_filter_builds_without_the_windows_project()
    {
        var root = RepoRoot();
        var slnf = File.ReadAllText(Path.Combine(root, "ContextAll.Linux.slnf"));
        Assert.DoesNotContain("App.Windows", slnf, StringComparison.Ordinal);
        var project = File.ReadAllText(Path.Combine(root, "src/ContextAll.App.Windows/ContextAll.App.Windows.csproj"));
        Assert.Contains("<WindowsPackageType>None</WindowsPackageType>", project, StringComparison.Ordinal);
        Assert.Contains("<PublishTrimmed>false</PublishTrimmed>", project, StringComparison.Ordinal);
        Assert.Contains("<PublishSingleFile>false</PublishSingleFile>", project, StringComparison.Ordinal);
        var manifest = File.ReadAllText(Path.Combine(root, "src/ContextAll.App.Android/AndroidManifest.xml"));
        Assert.Contains("connectedDevice", manifest, StringComparison.Ordinal);
        var activity = File.ReadAllText(Path.Combine(root, "src/ContextAll.App.Android/MainActivity.cs"));
        Assert.Contains("ListView", activity, StringComparison.Ordinal);
        Assert.DoesNotContain("PopupMenu", activity, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ContextAll.Linux.slnf")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repo");
    }
}
