using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ContextAll.Protocol;

namespace ContextAll.Host.Tests;

public sealed class TwoDaemonTests
{
    [Fact(Timeout = 180000)]
    public async Task Two_daemons_copy_text_and_resume_a_partial_file()
    {
        var (code, text) = await Reexec();
        Assert.True(code == 0 && text.Contains("scenario-ok", StringComparison.Ordinal), text);
    }

    public static async Task<int> RunScenario()
    {
        try
        {
            await Scenario();
            Console.WriteLine("scenario-ok");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static async Task<(int Code, string Text)> Reexec()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "unshare",
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-Urn");
        psi.ArgumentList.Add("timeout");
        psi.ArgumentList.Add("-k");
        psi.ArgumentList.Add("5");
        psi.ArgumentList.Add("150");
        psi.ArgumentList.Add("/usr/local/bin/dotnet");
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(typeof(TwoDaemonTests).Assembly.Location);
        psi.ArgumentList.Add("two-daemon");
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = (await stdout) + "\n" + (await stderr);
        return (process.ExitCode, text);
    }

    static async Task Scenario()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextall-two-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sleep = Process.Start(new ProcessStartInfo("unshare", "-n sleep 240")
        {
            UseShellExecute = false,
        })!;
        var left = NewSide(root, "a");
        var right = NewSide(root, "b");
        var host = typeof(DaemonProcess).Assembly.Location;
        Process? daemonA = null;
        Process? daemonB = null;
        try
        {
            Exec("/usr/sbin/ip", "link", "add", "vetha", "type", "veth", "peer", "name", "vethb");
            Exec("/usr/sbin/ip", "link", "set", "vethb", "netns", sleep.Id.ToString());
            Exec("/usr/sbin/ip", "addr", "add", "10.50.0.1/24", "dev", "vetha");
            Exec("/usr/sbin/ip", "link", "set", "vetha", "up");
            Exec("/usr/sbin/ip", "link", "set", "lo", "up");
            Exec("/usr/bin/nsenter", "-t", sleep.Id.ToString(), "-n", "/usr/sbin/ip", "addr", "add", "10.50.0.2/24", "dev", "vethb");
            Exec("/usr/bin/nsenter", "-t", sleep.Id.ToString(), "-n", "/usr/sbin/ip", "link", "set", "vethb", "up");
            Exec("/usr/bin/nsenter", "-t", sleep.Id.ToString(), "-n", "/usr/sbin/ip", "link", "set", "lo", "up");

            daemonA = StartDaemon(host, left, sleep.Id, inside: false);
            daemonB = StartDaemon(host, right, sleep.Id, inside: true);
            await WaitLog(daemonA, "clipboard-ready");
            await WaitLog(daemonB, "clipboard-ready");
            Console.Error.WriteLine("step ready");

            var peerOfA = await WaitSnapshot(left.Runtime, snap => snap.Devices.Length > 0);
            var peerOfB = await WaitSnapshot(right.Runtime, snap => snap.Devices.Length > 0);
            var bId = peerOfA.Devices[0].DeviceId;
            var aId = peerOfB.Devices[0].DeviceId;
            Console.Error.WriteLine("step discovered " + aId + " " + bId);
            Command(left.Runtime, FrameTypes.StartPairing, bId, null, null, null);
            Console.Error.WriteLine("step pairing requested");
            await WaitSnapshot(left.Runtime, snap => snap.Pairing is not null);
            await WaitSnapshot(right.Runtime, snap => snap.Pairing is not null);
            Command(left.Runtime, FrameTypes.AcceptPairing, bId, null, null, null);
            Command(right.Runtime, FrameTypes.AcceptPairing, aId, null, null, null);
            Console.Error.WriteLine("step accepted");
            await WaitSnapshot(left.Runtime, snap => snap.Devices.Any(device => device.DeviceId == bId && device.Trusted && device.Presence == "connected"));
            await WaitSnapshot(right.Runtime, snap => snap.Devices.Any(device => device.DeviceId == aId && device.Trusted && device.Presence == "connected"));
            Console.Error.WriteLine("step connected");

            var types = new List<byte>();
            using var watchStop = new CancellationTokenSource();
            var watch = Task.Run(() => Watch(right.Runtime, types, watchStop.Token));
            Assert.Equal(0, Command(left.Runtime, FrameTypes.SetClipboardSync, bId, null, true, null));
            Assert.Equal(0, Command(right.Runtime, FrameTypes.SetClipboardSync, aId, null, true, null));
            await WaitLog(daemonA, "clipboard-mutual");
            await WaitLog(daemonB, "clipboard-mutual");
            Console.Error.WriteLine("step mutual");
            left.Compositor.SetText("hello");
            Wait(() => right.Compositor.CurrentText == "hello");
            left.Compositor.SetText("");
            Wait(() => right.Compositor.CurrentText == "");
            Assert.False(File.Exists(Path.Combine(left.State, "contextall", "notifications.log")));
            Assert.False(File.Exists(Path.Combine(right.State, "contextall", "notifications.log")));
            watchStop.Cancel();
            try
            {
                await watch.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                // The watcher is blocked in a read that ends with the socket.
            }

            Assert.NotEmpty(types);
            Assert.All(types, type => Assert.True(type is >= 80 and <= 86 or 96));

            Console.Error.WriteLine("step copied");
            var rejected = Path.Combine(left.Home, "rejected.txt");
            File.WriteAllText(rejected, "nope");
            Assert.Equal(0, Command(left.Runtime, FrameTypes.SendFile, bId, rejected, null, null));
            var prompt = await WaitSnapshot(right.Runtime, snap => snap.FilePrompts.Length > 0);
            Assert.Empty(Directory.GetFiles(right.Downloads));
            Assert.Equal(0, Command(right.Runtime, FrameTypes.RejectFile, null, null, null, prompt.FilePrompts[0].TransferId));
            await WaitSnapshot(right.Runtime, snap => snap.FilePrompts.Length == 0);
            Assert.Empty(Directory.GetFiles(right.Downloads));
            Assert.Empty(Directory.GetFiles(Path.Combine(right.State, "contextall", "transfers"), "*.part"));

            var accepted = Path.Combine(left.Home, "accepted.txt");
            File.WriteAllText(accepted, "saved");
            Assert.Equal(0, Command(left.Runtime, FrameTypes.SendFile, bId, accepted, null, null));
            prompt = await WaitSnapshot(right.Runtime, snap => snap.FilePrompts.Length > 0);
            Assert.Equal(0, Command(right.Runtime, FrameTypes.AcceptFile, null, null, null, prompt.FilePrompts[0].TransferId));
            Wait(() => File.Exists(Path.Combine(right.Downloads, "accepted.txt")));
            Assert.Equal("saved", File.ReadAllText(Path.Combine(right.Downloads, "accepted.txt")));

            Assert.Equal(0, Command(right.Runtime, FrameTypes.SetAskBeforeSave, aId, null, false, null));
            var big = Path.Combine(left.Home, "resume.bin");
            var payload = new byte[TransferSize() * 4];
            Random.Shared.NextBytes(payload);
            File.WriteAllBytes(big, payload);
            Assert.Equal(0, Command(left.Runtime, FrameTypes.SendFile, bId, big, null, null));
            string? transferId = null;
            Wait(() =>
            {
                transferId = PartialId(right);
                return transferId is not null;
            });
            daemonA.Kill(entireProcessTree: true);
            await daemonA.WaitForExitAsync();
            var partial = transferId!;
            daemonA = StartDaemon(host, left, sleep.Id, inside: false);
            await WaitLog(daemonA, "clipboard-ready");
            Wait(() => File.Exists(Path.Combine(right.Downloads, "resume.bin")));
            Assert.Equal(payload, File.ReadAllBytes(Path.Combine(right.Downloads, "resume.bin")));
            Assert.False(File.Exists(Path.Combine(right.State, "contextall", "transfers", partial + ".part")));
        }
        catch
        {
            Directory.CreateDirectory("/opt/cursor/artifacts");
            File.WriteAllText("/opt/cursor/artifacts/two-daemon-a.log", LogOf(daemonA));
            File.WriteAllText("/opt/cursor/artifacts/two-daemon-b.log", LogOf(daemonB));
            throw;
        }
        finally
        {
            Kill(daemonA);
            Kill(daemonB);
            Kill(sleep);
            left.Compositor.Dispose();
            right.Compositor.Dispose();
        }
    }

    static Side NewSide(string root, string name)
    {
        var home = Path.Combine(root, name);
        var side = new Side(home, Path.Combine(home, "run"), Path.Combine(home, "state"), Path.Combine(home, "Downloads"));
        Directory.CreateDirectory(side.Runtime);
        Directory.CreateDirectory(side.Downloads);
        Directory.CreateDirectory(Path.Combine(home, "data"));
        Directory.CreateDirectory(Path.Combine(home, "config"));
        side.Compositor = new WlrCompositor(Path.Combine(home, "wayland.sock"));
        return side;
    }

    static Process StartDaemon(string host, Side side, int sleepPid, bool inside)
    {
        var psi = new ProcessStartInfo
        {
            FileName = inside ? "/usr/bin/nsenter" : "/usr/local/bin/dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (inside)
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(sleepPid.ToString());
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("/usr/local/bin/dotnet");
        }

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(host);
        psi.ArgumentList.Add("serve");
        psi.Environment["HOME"] = side.Home;
        psi.Environment["XDG_DATA_HOME"] = Path.Combine(side.Home, "data");
        psi.Environment["XDG_CONFIG_HOME"] = Path.Combine(side.Home, "config");
        psi.Environment["XDG_STATE_HOME"] = side.State;
        psi.Environment["XDG_RUNTIME_DIR"] = side.Runtime;
        psi.Environment["WAYLAND_DISPLAY"] = side.Compositor.Path;
        psi.Environment["CONTEXTALL_SEGMENT_PAUSE_MS"] = "500";
        var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, eventArgs) => Append(process, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Append(process, eventArgs.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    static void Append(Process process, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (process)
        {
            logs.TryAdd(process.Id, new StringBuilder());
            logs[process.Id].AppendLine(line);
        }
    }

    static readonly Dictionary<int, StringBuilder> logs = [];

    static string LogOf(Process? process) =>
        process is null || !logs.TryGetValue(process.Id, out var log) ? "" : log.ToString();

    static async Task WaitLog(Process process, string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (LogOf(process).Contains(text, StringComparison.Ordinal))
            {
                return;
            }

            if (process.HasExited)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.Fail(text + "\n" + LogOf(process));
    }

    static async Task<SnapshotPayload> WaitSnapshot(string runtime, Func<SnapshotPayload, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        SnapshotPayload? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                last = Snapshot(runtime);
                if (ready(last))
                {
                    return last;
                }
            }
            catch (IOException)
            {
                // The daemon is still opening the socket.
            }
            catch (SocketException)
            {
                // The daemon is still opening the socket.
            }

            await Task.Delay(100);
        }

        Assert.Fail(last is null ? "no snapshot" : JsonSerializer.Serialize(last));
        throw new InvalidOperationException("unreachable");
    }

    static SnapshotPayload Snapshot(string runtime)
    {
        using var stream = Open(runtime);
        var reader = new FrameReader(stream);
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(5))
        {
            var frame = reader.Next();
            if (frame.Type != FrameTypes.Snapshot)
            {
                continue;
            }

            var payload = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value
                ?? throw new IOException("snapshot");
            return payload with { Devices = payload.Devices ?? [] };
        }

        throw new TimeoutException("snapshot");
    }

    static int Command(string runtime, byte type, string? deviceId, string? path, bool? enabled, string? transferId)
    {
        using var stream = Open(runtime);
        var reader = new FrameReader(stream);
        var started = DateTime.UtcNow;
        while (reader.Next().Type != FrameTypes.Snapshot)
        {
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("snapshot");
            }
        }

        var payload = new LocalCommandPayload(Guid.NewGuid().ToString("N")[..8], deviceId, null, path, enabled, transferId);
        var encoded = FrameCodec.Encode(
            ControlJson.Encode(payload, ProtocolJsonContext.Default.LocalCommandPayload).Value!,
            type,
            0,
            0,
            0).Value!;
        stream.Write(encoded);
        stream.Flush();
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(8))
        {
            var reply = reader.Next();
            if (reply.Type != FrameTypes.CommandResult)
            {
                continue;
            }

            var result = ControlJson.Decode(reply.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value;
            if (result is not { Ok: true })
            {
                throw new InvalidOperationException((result?.Error ?? "invalid") + " type " + type);
            }

            return 0;
        }

        throw new TimeoutException("command " + type);
    }

    static void Watch(string runtime, List<byte> types, CancellationToken cancellationToken)
    {
        using var stream = Open(runtime);
        var reader = new FrameReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                types.Add(reader.Next().Type);
            }
            catch (IOException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
        }
    }

    static NetworkStream Open(string runtime)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.ReceiveTimeout = 3000;
        socket.SendTimeout = 3000;
        socket.Connect(new UnixDomainSocketEndPoint(Path.Combine(runtime, "contextall", "daemon.sock")));
        return new NetworkStream(socket, ownsSocket: true);
    }

    sealed class FrameReader(NetworkStream stream)
    {
        readonly byte[] buffer = new byte[FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes];
        int filled;

        public Frame Next()
        {
            while (true)
            {
                var read = FrameCodec.Read(buffer.AsSpan(0, filled));
                if (read.Frame is not null)
                {
                    var rest = filled - read.Consumed;
                    Buffer.BlockCopy(buffer, read.Consumed, buffer, 0, rest);
                    filled = rest;
                    return read.Frame;
                }

                if (read.IsFailure)
                {
                    throw new IOException(read.Reason);
                }

                var count = stream.Read(buffer, filled, buffer.Length - filled);
                if (count == 0)
                {
                    throw new IOException("no frame");
                }

                filled += count;
            }
        }
    }

    static string? PartialId(Side side)
    {
        var directory = Path.Combine(side.State, "contextall", "transfers");
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var meta in Directory.GetFiles(directory, "*.meta.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(meta));
            var root = document.RootElement;
            if (root.GetProperty("direction").GetString() != "receive")
            {
                continue;
            }

            var completed = root.GetProperty("completed").GetInt64();
            var count = root.GetProperty("segmentCount").GetInt64();
            if (completed > 0 && completed < count)
            {
                return root.GetProperty("transferId").GetString();
            }
        }

        return null;
    }

    static int TransferSize() => 65500;

    static void Wait(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (ready())
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail("timed out");
    }

    static void Exec(params string[] args)
    {
        var psi = new ProcessStartInfo(args[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.Join(' ', args) + "\n" + stdout + stderr);
        }
    }

    static void Kill(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ContextAll.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? "/workspace";
    }

    sealed class Side(string home, string runtime, string state, string downloads)
    {
        public string Home { get; } = home;

        public string Runtime { get; } = runtime;

        public string State { get; } = state;

        public string Downloads { get; } = downloads;

        public WlrCompositor Compositor { get; set; } = null!;
    }
}
