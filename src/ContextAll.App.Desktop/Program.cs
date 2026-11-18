using Avalonia;
using ContextAll.App;
using ContextAll.App.Platform;

namespace ContextAll.App.Desktop;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var selection = LinuxDisplaySelector.Select(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"));

        if (selection.Backend == LinuxDisplayBackend.None)
        {
            await Console.Error.WriteLineAsync(selection.ErrorMessage ?? LinuxDisplaySelector.NoDisplayMessage).ConfigureAwait(false);
            return 1;
        }

        var socketPath = SingleInstanceShell.SocketPath(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
        var token = Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN")
            ?? Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID");

        var acquisition = await SingleInstanceShell.TryAcquireOrActivateAsync(
            socketPath,
            token,
            activationToken => App.NotifyExternalActivation(activationToken)).ConfigureAwait(false);

        if (!acquisition.Acquired)
        {
            return 0;
        }

        await using var instance = acquisition.Instance;
        DesktopPlatform.Current = LinuxDesktopPlatform.Instance;
        await LinuxCursorEnvironment.ApplyAsync().ConfigureAwait(false);
        return Build(selection.Backend).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder Build() => Build(LinuxDisplayBackend.Wayland);

    public static AppBuilder Build(LinuxDisplayBackend backend)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        return backend switch
        {
            LinuxDisplayBackend.Wayland => builder
                .UseWayland()
                .With(new WaylandPlatformOptions { AppId = "app.contextall" }),
            LinuxDisplayBackend.X11 => builder
                .UseX11()
                .With(new X11PlatformOptions { EnableIme = true }),
            _ => builder,
        };
    }
}
