using Avalonia;
using ContextAll.App;

namespace ContextAll.App.Desktop;

public static class Program
{
    public static int Main(string[] args)
    {
        if (!WaylandSession.IsPresent(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Console.Error.WriteLine(WaylandSession.RequiredMessage);
            return 1;
        }

        return Build().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder Build() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWayland();
}
