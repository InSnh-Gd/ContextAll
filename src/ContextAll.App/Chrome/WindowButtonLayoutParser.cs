using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ContextAll.App.Chrome;

/// <summary>Standard window caption actions supported in CSD headers.</summary>
public enum WindowActionButton
{
    Minimize,
    Maximize,
    Close,
}

/// <summary>Resolved caption button placement and visibility for left and right clusters.</summary>
public sealed record WindowButtonLayout(
    IReadOnlyList<WindowActionButton> Left,
    IReadOnlyList<WindowActionButton> Right)
{
    public static WindowButtonLayout DefaultRight { get; } = new(
        [],
        [WindowActionButton.Minimize, WindowActionButton.Maximize, WindowActionButton.Close]);
}

public enum TitleBarStatus
{
    Application,
    Pending,
    System,
    Fallback,
}

/// <summary>Computed window chrome state for CSD vs native SSD and corner geometry.</summary>
public sealed record ChromeMode(
    WindowDecorations Decorations,
    bool ShowIntegratedHeader,
    CornerRadius OuterCornerRadius,
    Thickness ChromeBorderThickness,
    TitleBarStatus TitleBarStatus);

/// <summary>Computes Avalonia window decorations and rounded vs square corner geometry.</summary>
public static class ChromeModeState
{
    public const double NormalCornerRadius = 16.0;

    public static ChromeMode Compute(
        bool useNativeTitleBar,
        WindowState windowState,
        bool isTiled = false,
        bool systemTitleBarActive = false,
        bool nativeDecorationsSettled = true)
    {
        if (useNativeTitleBar && systemTitleBarActive)
        {
            return new ChromeMode(WindowDecorations.Full, false, new CornerRadius(0), new Thickness(0), TitleBarStatus.System);
        }

        var squareCorners = isTiled || windowState is WindowState.Maximized or WindowState.FullScreen;
        return new ChromeMode(
            WindowDecorations.BorderOnly,
            true,
            squareCorners ? new CornerRadius(0) : new CornerRadius(NormalCornerRadius),
            new Thickness(1),
            !useNativeTitleBar ? TitleBarStatus.Application
                : nativeDecorationsSettled ? TitleBarStatus.Fallback : TitleBarStatus.Pending);
    }

    /// <summary>
    /// True when the platform is about to paint its own caption instead of the window manager.
    /// Linux native title bar must not fall through to that Win32-style overlay.
    /// </summary>
    public static bool PlatformPaintsManagedCaption(object? platformImpl)
    {
        if (platformImpl is null)
        {
            return false;
        }

        var type = platformImpl.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (type.GetProperty("NeedsManagedDecorations", flags)?.GetValue(platformImpl) is true)
        {
            return true;
        }

        var requested = type.GetProperty("RequestedDrawnDecorations", flags)?.GetValue(platformImpl);
        if (requested is null)
        {
            return false;
        }

        const int titleBarBit = 4;
        try
        {
            return (Convert.ToInt32(requested, CultureInfo.InvariantCulture) & titleBarBit) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True when Avalonia has inserted its managed caption overlay into the window visual tree.
    /// </summary>
    public static bool VisualTreeHasManagedCaption(Visual? root)
    {
        if (root is null)
        {
            return false;
        }

        foreach (var visual in root.GetVisualDescendants())
        {
            var name = visual.GetType().Name;
            if (name is "WindowDrawnDecorations" or "CaptionButtons")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Subscribes to the platform's drawn-decorations request callback when the member is available.
    /// </summary>
    public static bool TrySubscribeDrawnDecorationsChanged(object? platformImpl, Action callback)
    {
        if (platformImpl is null)
        {
            return false;
        }

        try
        {
            var type = platformImpl.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var evt = type.GetEvent("DrawnDecorationsRequestChanged", flags);
            if (evt is not null)
            {
                evt.AddEventHandler(platformImpl, callback);
                return true;
            }

            var prop = type.GetProperty("DrawnDecorationsRequestChanged", flags);
            if (prop is null || !prop.CanRead || !prop.CanWrite)
            {
                return false;
            }

            if (prop.GetValue(platformImpl) is not Action existing)
            {
                prop.SetValue(platformImpl, callback);
                return true;
            }

            prop.SetValue(platformImpl, existing + callback);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Parses GNOME gsettings button-layout and KDE kwinrc decoration button configs.</summary>
public static class WindowButtonLayoutParser
{
    public static bool TryParseGnome(string? raw, out WindowButtonLayout layout)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            layout = WindowButtonLayout.DefaultRight;
            return false;
        }

        var cleaned = raw.Trim().Trim('\'', '"').Trim();
        if (cleaned.Length == 0)
        {
            layout = WindowButtonLayout.DefaultRight;
            return false;
        }

        var colon = cleaned.IndexOf(':');
        var leftRaw = colon >= 0 ? cleaned[..colon] : cleaned;
        var rightRaw = colon >= 0 ? cleaned[(colon + 1)..] : string.Empty;

        var seen = new HashSet<WindowActionButton>();
        var left = ParseGnomeTokens(leftRaw, seen);
        var right = ParseGnomeTokens(rightRaw, seen);
        layout = new WindowButtonLayout(left, right);
        return true;
    }

    public static bool TryParseKdeKwinrc(string? content, out WindowButtonLayout layout)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            layout = WindowButtonLayout.DefaultRight;
            return false;
        }

        var inDecorationSection = false;
        var sawKey = false;
        var leftRaw = string.Empty;
        var rightRaw = string.Empty;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                inDecorationSection = section.Equals("org.kde.kdecoration2", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDecorationSection)
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (key.Equals("ButtonsOnLeft", StringComparison.OrdinalIgnoreCase))
            {
                leftRaw = val;
                sawKey = true;
            }
            else if (key.Equals("ButtonsOnRight", StringComparison.OrdinalIgnoreCase))
            {
                rightRaw = val;
                sawKey = true;
            }
        }

        if (!sawKey)
        {
            layout = WindowButtonLayout.DefaultRight;
            return false;
        }

        var seen = new HashSet<WindowActionButton>();
        var left = ParseKdeCodes(leftRaw, seen);
        var right = ParseKdeCodes(rightRaw, seen);
        layout = new WindowButtonLayout(left, right);
        return true;
    }

    public static WindowButtonLayout Resolve(
        string? xdgCurrentDesktop,
        string? gnomeButtonLayout,
        string? kwinrcContent)
    {
        var preferKde = !string.IsNullOrWhiteSpace(xdgCurrentDesktop) &&
                        xdgCurrentDesktop.Contains("KDE", StringComparison.OrdinalIgnoreCase);

        if (preferKde)
        {
            if (TryParseKdeKwinrc(kwinrcContent, out var kdeLayout))
            {
                return kdeLayout;
            }

            if (TryParseGnome(gnomeButtonLayout, out var gnomeFallback))
            {
                return gnomeFallback;
            }
        }
        else
        {
            if (TryParseGnome(gnomeButtonLayout, out var gnomeLayout))
            {
                return gnomeLayout;
            }

            if (TryParseKdeKwinrc(kwinrcContent, out var kdeFallback))
            {
                return kdeFallback;
            }
        }

        return WindowButtonLayout.DefaultRight;
    }

    public static async Task<WindowButtonLayout> ProbeCurrentDesktopAsync(CancellationToken ct = default)
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        var kwinrc = await ReadKwinrcAsync(ct);
        var gnome = await ReadGnomeButtonLayoutAsync(ct);
        return Resolve(desktop, gnome, kwinrc);
    }

    private static List<WindowActionButton> ParseGnomeTokens(string slice, HashSet<WindowActionButton> seen)
    {
        var result = new List<WindowActionButton>();
        foreach (var raw in slice.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            WindowActionButton? mapped = raw.ToLowerInvariant() switch
            {
                "minimize" => WindowActionButton.Minimize,
                "maximize" => WindowActionButton.Maximize,
                "close" => WindowActionButton.Close,
                _ => null,
            };

            if (mapped is { } button && seen.Add(button))
            {
                result.Add(button);
            }
        }

        return result;
    }

    private static List<WindowActionButton> ParseKdeCodes(string codes, HashSet<WindowActionButton> seen)
    {
        var result = new List<WindowActionButton>();
        foreach (var ch in codes)
        {
            WindowActionButton? mapped = char.ToUpperInvariant(ch) switch
            {
                'I' => WindowActionButton.Minimize,
                'A' => WindowActionButton.Maximize,
                'X' => WindowActionButton.Close,
                _ => null,
            };

            if (mapped is { } button && seen.Add(button))
            {
                result.Add(button);
            }
        }

        return result;
    }

    private static async Task<string?> ReadKwinrcAsync(CancellationToken ct)
    {
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(xdgConfig))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                return null;
            }

            xdgConfig = Path.Combine(home, ".config");
        }

        var path = Path.Combine(xdgConfig, "kwinrc");
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

    private static async Task<string?> ReadGnomeButtonLayoutAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gsettings",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("get");
            psi.ArgumentList.Add("org.gnome.desktop.wm.preferences");
            psi.ArgumentList.Add("button-layout");

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(900));
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
