using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Ipc;

namespace ContextAll.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        _ = CoreRules.MaxPayloadBytes;
        _ = SharedEffects.PayloadCap();
        _ = LocalChannelClient.ForThisPlatform();
        var io = new CliIo
        {
            Out = Console.Out,
            Error = Console.Error,
            IsTerminal = !Console.IsInputRedirected,
            RuntimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
        };
        return await CliApp.Run(args, io).ConfigureAwait(false);
    }
}
