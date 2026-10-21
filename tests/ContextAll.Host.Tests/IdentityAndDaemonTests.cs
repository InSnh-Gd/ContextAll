using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ContextAll.Host;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.Host.Tests;

public sealed class IdentityAndDaemonTests
{
    [Fact]
    public void A_missing_key_pair_is_created_on_the_linux_path_and_a_mismatch_is_kept()
    {
        var root = Temp();
        var home = Path.Combine(root, "home");
        var data = Path.Combine(root, "data");
        var directory = LinuxPaths.IdentityDirectory(data, home);
        var started = IdentityStore.Start(directory);

        Assert.True(started.Ok);
        Assert.True(File.Exists(Path.Combine(directory, "device.key")));
        Assert.True(File.Exists(Path.Combine(directory, "device.spki")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, "device.key")));

        var key = File.ReadAllBytes(Path.Combine(directory, "device.key"));
        File.Delete(Path.Combine(directory, "device.spki"));
        Assert.False(IdentityStore.Start(directory).Ok);
        Assert.False(File.Exists(Path.Combine(directory, "device.spki")));
        Assert.Equal(key, File.ReadAllBytes(Path.Combine(directory, "device.key")));

        File.WriteAllBytes(Path.Combine(directory, "device.spki"), [1, 2, 3]);
        var before = File.ReadAllBytes(Path.Combine(directory, "device.key"));
        Assert.False(IdentityStore.Start(directory).Ok);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(directory, "device.key")));
    }

    [Fact]
    public void A_missing_settings_file_is_created_and_an_unreadable_file_is_kept()
    {
        var root = Temp();
        var path = LinuxPaths.SettingsFile(Path.Combine(root, "config"), Path.Combine(root, "home"));
        var created = SettingsStore.Start(path, "desk");
        var text = File.ReadAllText(path);

        Assert.True(created.Ok);
        Assert.Empty(created.Document!.Trust);
        Assert.Equal("", created.Document.DownloadDirectory);
        Assert.DoesNotContain("PRIVATE KEY", text, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Path.Combine(root, "home"), "Downloads"), SettingsStore.ResolveDownload("", Path.Combine(root, "home")));

        created.Document.Trust.Add(new TrustDocument { DeviceId = "ca1example", DisplayName = "phone", Spki = "aaaa", ClipboardSync = false, AskBeforeSave = true });
        SettingsStore.Write(path, created.Document);
        text = File.ReadAllText(path);
        Assert.Contains("ClipboardSync: false", text, StringComparison.Ordinal);
        Assert.Contains("AskBeforeSave: true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.", text, StringComparison.Ordinal);

        var broken = ":\n: [\n"u8.ToArray();
        File.WriteAllBytes(path, broken);
        Assert.False(SettingsStore.Start(path, "desk").Ok);
        Assert.Equal(broken, File.ReadAllBytes(path));
    }

    [Fact]
    public void Reset_replaces_a_confirmed_key_and_leaves_a_declined_key_in_place()
    {
        var root = Temp();
        var identity = LinuxPaths.IdentityDirectory(Path.Combine(root, "data"), Path.Combine(root, "home"));
        var settings = LinuxPaths.SettingsFile(Path.Combine(root, "config"), Path.Combine(root, "home"));
        var transfers = Path.Combine(LinuxPaths.StateDirectory(Path.Combine(root, "state"), Path.Combine(root, "home")), "transfers");
        Directory.CreateDirectory(transfers);
        File.WriteAllText(Path.Combine(transfers, "a.part"), "partial");
        IdentityStore.Start(identity);
        var settingsDoc = SettingsStore.Start(settings, "kept-name");
        settingsDoc.Document!.Trust.Add(new TrustDocument { DeviceId = "ca1x", DisplayName = "p", Spki = "aa", AskBeforeSave = true });
        SettingsStore.Write(settings, settingsDoc.Document);
        var key = File.ReadAllBytes(Path.Combine(identity, "device.key"));

        Assert.Equal(1, IdentityReset.Run(identity, settings, transfers, interactive: false, confirmed: false, operatorSaidYes: false));
        Assert.Equal(key, File.ReadAllBytes(Path.Combine(identity, "device.key")));
        Assert.Equal(1, IdentityReset.Run(identity, settings, transfers, interactive: true, confirmed: false, operatorSaidYes: false));
        Assert.Equal(key, File.ReadAllBytes(Path.Combine(identity, "device.key")));

        Assert.Equal(0, IdentityReset.Run(identity, settings, transfers, interactive: false, confirmed: true, operatorSaidYes: false));
        Assert.NotEqual(key, File.ReadAllBytes(Path.Combine(identity, "device.key")));
        Assert.Empty(SettingsStore.Start(settings, "ignored").Document!.Trust);
        Assert.Equal("kept-name", SettingsStore.Start(settings, "ignored").Document!.DisplayName);
        Assert.False(File.Exists(Path.Combine(transfers, "a.part")));

        var goodKey = File.ReadAllBytes(Path.Combine(identity, "device.key"));
        File.WriteAllText(settings, ":\n: [\n");
        Assert.Equal(0, IdentityReset.Run(identity, settings, transfers, interactive: false, confirmed: true, operatorSaidYes: false));
        Assert.Equal(goodKey, File.ReadAllBytes(Path.Combine(identity, "device.key")));
        Assert.Empty(SettingsStore.Start(settings, "ignored").Document!.Trust);
    }

    [Fact]
    public async Task A_second_serve_exits_and_a_command_does_not_start_the_daemon()
    {
        var root = Temp();
        var starts = DaemonProcess.Starts;
        using var cts = new CancellationTokenSource();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = DaemonProcess.Serve(root, _ =>
        {
            listening.TrySetResult();
            return Task.Delay(Timeout.Infinite, cts.Token);
        }, cts.Token);
        await listening.Task;
        Assert.Equal(1, await DaemonProcess.Serve(root, null, CancellationToken.None));
        using (var client = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified))
        {
            client.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(LocalChannelClient.LinuxSocket(root)));
        }

        cts.Cancel();
        await serve;

        var stale = Temp();
        var socket = LocalChannelClient.LinuxSocket(stale);
        Directory.CreateDirectory(Path.GetDirectoryName(socket)!);
        File.WriteAllText(socket, "stale");
        using var cts2 = new CancellationTokenSource();
        var listening2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var again = DaemonProcess.Serve(stale, _ =>
        {
            listening2.TrySetResult();
            return Task.Delay(Timeout.Infinite, cts2.Token);
        }, cts2.Token);
        await listening2.Task;
        Assert.True(DaemonProcess.Probe(socket));
        cts2.Cancel();
        await again;

        var io = new CliIo
        {
            Out = new StringWriter(),
            Error = new StringWriter(),
            IsTerminal = false,
            RuntimeDirectory = Temp(),
        };
        var before = DaemonProcess.Starts;
        Assert.NotEqual(0, await CliApp.Run(["device", "list"], io));
        Assert.Contains("daemon", io.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, DaemonProcess.Starts);
        var usage = new StringWriter();
        Assert.NotEqual(0, await CliApp.Run(["pair"], io with { Error = usage }));
        Assert.Contains("Usage", usage.ToString(), StringComparison.Ordinal);
        var opened = false;
        await CliApp.Run(["device", "list"], io with { IsTerminal = true }, () =>
        {
            opened = true;
            return 0;
        });
        Assert.False(opened);
        Assert.True(CliMenu.ShouldOpen([], true));
        Assert.Contains("Pair", CliMenu.Actions);
        _ = starts;
    }

    [Fact]
    public void A_missing_clipboard_file_does_not_stop_listening_and_the_window_is_empty()
    {
        var root = Temp();
        var path = ClipboardFiles.PathFor(Path.Combine(root, "state"), Path.Combine(root, "home"));
        Assert.True(ClipboardFiles.Listen(path, "hello", true));
        Assert.DoesNotContain("window", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        var stored = ClipboardFiles.Read(path);
        var same = ClipboardFiles.Listen(path, "hello", true);
        Assert.True(same);
        Assert.Equal(stored.Sequence, ClipboardFiles.Read(path).Sequence);
        Assert.Equal(0, ContextAll.Core.ClipboardRestart.Plan(stored, false, "hello", true).WindowCount);
        var sequence = ClipboardFiles.Read(path).Sequence;
        Assert.True(ClipboardFiles.Listen(path, "brand-new", true));
        var changed = ClipboardFiles.Read(path);
        Assert.Equal(sequence + 1, changed.Sequence);
        Assert.Equal("brand-new", changed.Text);
        Assert.Equal("brand-new", changed.AppliedText);
    }

    [Fact]
    public async Task A_desktop_client_frame_decodes_on_the_local_socket()
    {
        Assert.Equal(1, await DaemonProcess.Serve(null, null, CancellationToken.None));
        Assert.Equal(1, await DaemonProcess.Serve("", null, CancellationToken.None));
        var file = Path.Combine(Temp(), "not-a-directory");
        File.WriteAllText(file, "x");
        Assert.Equal(1, await DaemonProcess.Serve(file, null, CancellationToken.None));
        Assert.False(File.Exists("/tmp/contextall/daemon.sock"));

        var root = Temp();
        using var cts = new CancellationTokenSource();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = DaemonProcess.Serve(root, _ =>
        {
            listening.TrySetResult();
            return Task.Delay(Timeout.Infinite, cts.Token);
        }, cts.Token);
        await listening.Task;
        var socketPath = LocalChannelClient.LinuxSocket(root);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(socketPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(socketPath)!));

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(socketPath));
        var snapshot = ReadFrame(client);
        Assert.Equal(FrameTypes.Snapshot, snapshot.Type);
        var command = FrameCodec.Encode(
            ControlJson.Encode(
                new LocalCommandPayload("ab", null, "Desk", null, null, null),
                ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            FrameTypes.SetDisplayName,
            0,
            0,
            0).Value!;
        client.Send(command);
        var result = ReadFrame(client);
        Assert.Equal(96, result.Type);
        cts.Cancel();
        await serve;

        Assert.Equal(@"\\.\pipe\ContextAll\user-1", LocalChannelClient.WindowsPipe("user-1"));
        Assert.Equal(@"ContextAll\user-1", DaemonProcess.PipeName("user-1"));
    }

    [Fact]
    public async Task A_windows_pipe_decodes_a_client_frame_and_a_second_serve_exits()
    {
        using var cts = new CancellationTokenSource();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = DaemonProcess.ServePipe("user-1", _ =>
        {
            listening.TrySetResult();
            return Task.Delay(Timeout.Infinite, cts.Token);
        }, cts.Token);
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var client = new NamedPipeClientStream(".", DaemonProcess.PipeName("user-1"), PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        var snapshot = await ReadStream(client);
        Assert.Equal(FrameTypes.Snapshot, snapshot.Type);
        Assert.Equal(1, await DaemonProcess.ServePipe("user-1", null, CancellationToken.None));
        cts.Cancel();
        await serve;
    }

    static async Task<Frame> ReadStream(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[FrameLimits.HeaderBytes + 4096];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(filled), timeout.Token);
            if (count == 0)
            {
                break;
            }

            filled += count;
            var read = FrameCodec.Read(buffer.AsSpan(0, filled));
            if (read.Frame is not null)
            {
                return read.Frame;
            }

            if (read.IsFailure)
            {
                throw new InvalidOperationException(read.Reason);
            }
        }

        throw new InvalidOperationException("no frame");
    }

    static Frame ReadFrame(Socket client)
    {
        client.ReceiveTimeout = 3000;
        var buffer = new byte[FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            int count;
            try
            {
                count = client.Receive(buffer, filled, buffer.Length - filled, SocketFlags.None);
            }
            catch (SocketException)
            {
                break;
            }

            if (count == 0)
            {
                break;
            }

            filled += count;
            var read = FrameCodec.Read(buffer.AsSpan(0, filled));
            if (read.Frame is not null)
            {
                return read.Frame;
            }

            if (read.IsFailure)
            {
                throw new InvalidOperationException(read.Reason);
            }
        }

        throw new InvalidOperationException("no frame");
    }

    [Fact]
    public async Task Serve_reports_ready_without_a_wayland_display()
    {
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var notify = Environment.GetEnvironmentVariable("NOTIFY_SOCKET");
        var root = Temp();
        var socketPath = Path.Combine(root, "notify.sock");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(socketPath));
        socket.ReceiveTimeout = 3000;
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        Environment.SetEnvironmentVariable("NOTIFY_SOCKET", socketPath);
        try
        {
            using var cts = new CancellationTokenSource();
            var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var serve = DaemonProcess.Serve(Path.Combine(root, "run"), _ =>
            {
                listening.TrySetResult();
                return Task.Delay(Timeout.Infinite, cts.Token);
            }, cts.Token);
            await listening.Task;
            var buffer = new byte[32];
            var count = socket.Receive(buffer);
            Assert.Equal("READY=1", Encoding.ASCII.GetString(buffer, 0, count));
            cts.Cancel();
            await serve;
        }
        finally
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", wayland);
            Environment.SetEnvironmentVariable("NOTIFY_SOCKET", notify);
        }
    }

    [Fact]
    public void The_appimage_launcher_sends_serve_to_the_host_and_other_args_to_the_cli()
    {
        var root = Temp();
        var source = Path.Combine(RepoRoot(), "packaging/appimage/AppRun");
        var launcher = Path.Combine(root, "AppRun");
        File.Copy(source, launcher);
        File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        WriteStub(root, "contextall-shell", "shell");
        WriteStub(root, "contextall-host", "host");
        var record = Path.Combine(root, "record");
        Assert.Equal("shell", RunLauncher(root, launcher, record, []));
        Assert.Equal("host serve", RunLauncher(root, launcher, record, ["serve"]));
        Assert.Equal("host device list", RunLauncher(root, launcher, record, ["device", "list"]));
        var unit = File.ReadAllText(Path.Combine(RepoRoot(), "packaging/contextall.service"));
        Assert.Contains("Type=notify", unit, StringComparison.Ordinal);
        Assert.Contains("WantedBy=default.target", unit, StringComparison.Ordinal);
        Assert.Contains("ContextAll.AppImage serve", unit, StringComparison.Ordinal);
    }

    static void WriteStub(string root, string name, string label)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s %s\\n' " + label + " \"$*\" > \"$APP_RECORD\"\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    static string RunLauncher(string root, string launcher, string record, string[] args)
    {
        if (File.Exists(record))
        {
            File.Delete(record);
        }

        var start = new ProcessStartInfo(launcher)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["APP_RECORD"] = record;
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start);
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(3000));
        Assert.Equal(0, process.ExitCode);
        return File.ReadAllText(record).Trim();
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ContextAll.Linux.slnf")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repo");
    }

    static string Temp() => Directory.CreateTempSubdirectory("contextall-").FullName;
}
