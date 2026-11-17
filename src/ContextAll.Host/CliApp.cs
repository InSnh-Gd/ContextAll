using System.CommandLine;
using Spectre.Console;

namespace ContextAll.Host;

public sealed record CliIo
{
    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public required bool IsTerminal { get; init; }

    public required string? RuntimeDirectory { get; init; }
}

public static class CliMenu
{
    public static readonly string[] Actions = ["List devices", "Pair", "Connect endpoint", "Clipboard", "Ask before save", "Send file"];

    public static bool ShouldOpen(IReadOnlyList<string> args, bool stdinIsTerminal) =>
        args.Count == 0 && stdinIsTerminal;
}

public static class CliApp
{
    public static async Task<int> Run(string[] args, CliIo io, Func<int>? menu = null)
    {
        if (CliMenu.ShouldOpen(args, io.IsTerminal))
        {
            return menu?.Invoke() ?? OpenSpectreMenu();
        }

        if (args is ["serve"])
        {
            return await DaemonProcess.ServeDefault(io.RuntimeDirectory, null, CancellationToken.None).ConfigureAwait(false);
        }

        var root = new RootCommand("ContextAll");
        var serve = new Command("serve", "Listen for local front ends");
        serve.SetAction(_ => 0);
        var list = new Command("list", "List devices");
        var device = new Command("device", "Devices");
        device.Subcommands.Add(list);
        list.SetAction(_ => LocalClient.DeviceList(io));
        var pairId = new Argument<string>("device-id");
        var pair = new Command("pair", "Start pairing");
        pair.Arguments.Add(pairId);
        pair.SetAction(result => LocalClient.Pair(io, result.GetValue(pairId) ?? ""));
        var connectEndpoint = new Argument<string>("endpoint");
        var connectAlias = new Argument<string>("alias")
        {
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "",
        };
        var connect = new Command("connect", "Connect directly to a peer endpoint");
        connect.Arguments.Add(connectEndpoint);
        connect.Arguments.Add(connectAlias);
        connect.SetAction(result => LocalClient.Connect(io, result.GetValue(connectEndpoint) ?? "", result.GetValue(connectAlias)));
        var clipboardId = new Argument<string>("device-id");
        var clipboardFlag = new Argument<string>("switch");
        var clipboard = new Command("clipboard", "Set clipboard sync");
        clipboard.Arguments.Add(clipboardId);
        clipboard.Arguments.Add(clipboardFlag);
        clipboard.SetAction(result => LocalClient.Clipboard(io, result.GetValue(clipboardId) ?? "", result.GetValue(clipboardFlag) ?? ""));
        var askId = new Argument<string>("device-id");
        var askFlag = new Argument<string>("switch");
        var ask = new Command("ask-before-save", "Set ask-before-save");
        ask.Arguments.Add(askId);
        ask.Arguments.Add(askFlag);
        ask.SetAction(result => LocalClient.AskBeforeSave(io, result.GetValue(askId) ?? "", result.GetValue(askFlag) ?? ""));
        var sendId = new Argument<string>("device-id");
        var sendPath = new Argument<string>("path");
        var send = new Command("send", "Send a file");
        send.Arguments.Add(sendId);
        send.Arguments.Add(sendPath);
        var file = new Command("file", "Files");
        file.Subcommands.Add(send);
        send.SetAction(result => LocalClient.SendFile(io, result.GetValue(sendId) ?? "", result.GetValue(sendPath) ?? ""));
        var yes = new Option<bool>("--yes");
        var reset = new Command("reset", "Replace this device key");
        reset.Options.Add(yes);
        var identity = new Command("identity", "Identity");
        identity.Subcommands.Add(reset);
        reset.SetAction(result =>
        {
            var paths = HostPaths.ForThisPlatform();
            return IdentityReset.Run(
                paths.IdentityDirectory,
                paths.SettingsFile,
                paths.TransfersDirectory,
                io.IsTerminal,
                result.GetValue(yes),
                operatorSaidYes: false);
        });
        root.Subcommands.Add(serve);
        root.Subcommands.Add(device);
        root.Subcommands.Add(pair);
        root.Subcommands.Add(connect);
        root.Subcommands.Add(clipboard);
        root.Subcommands.Add(ask);
        root.Subcommands.Add(file);
        root.Subcommands.Add(identity);
        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            io.Error.WriteLine(parsed.ToString());
            io.Error.WriteLine("Usage: contextall <command>");
            return 1;
        }

        var code = parsed.Invoke();
        return code;
    }

    public static int OpenSpectreMenu()
    {
        AnsiConsole.Prompt(new SelectionPrompt<string>().Title("ContextAll").AddChoices(CliMenu.Actions));
        return 0;
    }
}
