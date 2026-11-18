namespace ContextAll.App.Platform;

public static class DisplayServerLabels
{
    public static string Resolve(string? waylandDisplay, string? x11Display, bool isWindows = false)
    {
        if (isWindows)
        {
            return "Win32";
        }

        if (!string.IsNullOrWhiteSpace(waylandDisplay))
        {
            return $"Wayland ({waylandDisplay})";
        }

        if (!string.IsNullOrWhiteSpace(x11Display))
        {
            return $"X11 ({x11Display})";
        }

        return "未检测到图形显示会话";
    }
}
