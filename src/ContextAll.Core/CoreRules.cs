using ContextAll.Protocol;

namespace ContextAll.Core;

public static class CoreRules
{
    public static int MaxPayloadBytes => FrameLimits.MaxPayloadBytes;
}
