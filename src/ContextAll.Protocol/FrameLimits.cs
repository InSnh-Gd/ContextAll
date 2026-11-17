namespace ContextAll.Protocol;

public static class FrameLimits
{
    public const int HeaderBytes = 16;
    public const int MaxPayloadBytes = 64 * 1024;
    public const int MaxClipboardBytes = 1024 * 1024;
}
