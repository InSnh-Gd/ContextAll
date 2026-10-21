using ContextAll.Ipc;

namespace ContextAll.App;

public static class AppShell
{
    public static LocalChannelClient Client() => LocalChannelClient.ForThisPlatform();
}
