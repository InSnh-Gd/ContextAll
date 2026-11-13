using ContextAll.Effects;

namespace ContextAll.App.Android;

public static class AndroidSession
{
    public static int Run() => SharedEffects.PayloadCap();
}
