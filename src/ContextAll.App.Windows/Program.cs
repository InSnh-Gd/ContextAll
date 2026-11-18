using Avalonia;
using ContextAll.App;
using ContextAll.App.Platform;
using ContextAll.Ipc;

namespace ContextAll.App.Windows;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        DesktopPlatform.Current = WindowsDesktopPlatform.Instance;

        var sid = SanitizeSid(LocalChannelClient.CurrentUserSid());
        var mutexName = "Local\\ContextAll-shell-" + sid;
        var pipeName = @"ContextAll-shell\" + LocalChannelClient.CurrentUserSid();

        var acquisition = await PipeSingleInstance.TryAcquireOrActivateAsync(
            mutexName,
            pipeName,
            activationToken: null,
            token => App.NotifyExternalActivation(token)).ConfigureAwait(false);

        if (!acquisition.Acquired || acquisition.Instance is not { } instance)
        {
            return 0;
        }

        await using (instance)
        {
            return Build().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder Build() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWin32();

    private static string SanitizeSid(string sid) =>
        sid.Replace('\\', '-').Replace('/', '-');
}
