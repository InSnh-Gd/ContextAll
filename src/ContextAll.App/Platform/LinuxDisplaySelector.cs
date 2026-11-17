namespace ContextAll.App.Platform;

public enum LinuxDisplayBackend
{
    None,
    Wayland,
    X11,
}

public sealed record DisplaySelection(LinuxDisplayBackend Backend, string? ErrorMessage);

public static class LinuxDisplaySelector
{
    public const string NoDisplayMessage =
        "未检测到图形显示会话（请设置 WAYLAND_DISPLAY 或 DISPLAY）。";

    public static DisplaySelection Select(string? waylandDisplay, string? x11Display)
    {
        if (!string.IsNullOrWhiteSpace(waylandDisplay))
        {
            return new DisplaySelection(LinuxDisplayBackend.Wayland, null);
        }

        if (!string.IsNullOrWhiteSpace(x11Display))
        {
            return new DisplaySelection(LinuxDisplayBackend.X11, null);
        }

        return new DisplaySelection(LinuxDisplayBackend.None, NoDisplayMessage);
    }
}
