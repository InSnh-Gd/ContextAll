namespace ContextAll.App;

public static class WaylandSession
{
    public const string RequiredMessage = "Wayland is required.";

    public static bool IsPresent(string? waylandDisplay) => !string.IsNullOrEmpty(waylandDisplay);
}
