using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Ipc;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host.Tests;

[Collection("DiscoverySockets")]
public sealed class WindowsDaemonTests
{
    [Fact]
    public void Channel_server_selects_pipe_on_windows_and_socket_elsewhere()
    {
        var pipe = ChannelServers.ForPlatform(windows: true, runtimeDirectory: null, userSid: "S-1-5-21-1-2-3-1001");
        var socket = ChannelServers.ForPlatform(windows: false, runtimeDirectory: "/run/user/1000", userSid: "unused");

        Assert.Equal(LocalTransport.NamedPipe, pipe.Transport);
        Assert.IsType<PipeChannelServer>(pipe);
        Assert.Equal(LocalTransport.UnixSocket, socket.Transport);
        Assert.IsType<UnixChannelServer>(socket);
    }

    [Fact]
    public async Task Pipe_daemon_serves_two_clients_the_same_snapshot()
    {
        var root = Directory.CreateTempSubdirectory("contextall-pipe-").FullName;
        var sid = "wd" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = DaemonProcess.ServePipe(
            sid,
            () => LanHost.CreateIsolated(root),
            _ =>
            {
                listening.TrySetResult();
                return Task.Delay(Timeout.Infinite, cts.Token);
            },
            cts.Token);
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var first = await ConnectPipe(sid);
        await using var second = await ConnectPipe(sid);
        var snapshotA = await ReadStream(first);
        var snapshotB = await ReadStream(second);
        Assert.Equal(FrameTypes.Snapshot, snapshotA.Type);
        Assert.Equal(FrameTypes.Snapshot, snapshotB.Type);
        var payloadA = ControlJson.Decode(snapshotA.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value;
        var payloadB = ControlJson.Decode(snapshotB.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value;
        Assert.NotNull(payloadA);
        Assert.NotNull(payloadB);
        Assert.Equal(payloadA.LocalDeviceId, payloadB.LocalDeviceId);
        Assert.False(string.IsNullOrEmpty(payloadA.LocalDeviceId));

        cts.Cancel();
        await serve;
    }

    [Fact]
    public async Task Second_pipe_serve_exits_when_the_name_is_taken()
    {
        var root = Directory.CreateTempSubdirectory("contextall-pipe-").FullName;
        var sid = "wd" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = DaemonProcess.ServePipe(
            sid,
            () => LanHost.CreateIsolated(root),
            _ =>
            {
                listening.TrySetResult();
                return Task.Delay(Timeout.Infinite, cts.Token);
            },
            cts.Token);
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await DaemonProcess.ServePipe(sid, () => LanHost.CreateIsolated(root + "-b"), null, CancellationToken.None));
        cts.Cancel();
        await serve;
    }

    [Fact]
    public void Pipe_security_grants_the_current_user_and_denies_everyone()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.NotNull(typeof(PipeAcl).GetMethod(nameof(PipeAcl.CurrentUserOnly)));
            return;
        }

        const string sid = "S-1-5-21-3623811015-3361044348-30300820-1013";
        var security = PipeAcl.CurrentUserOnly(sid);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Assert.Contains(rules, rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl)
            && rule.IdentityReference.Value == sid);
        Assert.Contains(rules, rule =>
            rule.AccessControlType == AccessControlType.Deny
            && rule.IdentityReference.Value == "S-1-1-0");
    }

    [Fact]
    public async Task Windows_serve_starts_without_xdg_variables()
    {
        var root = Directory.CreateTempSubdirectory("contextall-xdg-").FullName;
        var sid = "wd" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = DaemonProcess.ServeDefault(
            runtimeDirectory: null,
            hostFactory: () => LanHost.CreateIsolated(root),
            onListening: _ =>
            {
                listening.TrySetResult();
                return Task.Delay(Timeout.Infinite, cts.Token);
            },
            cancellationToken: cts.Token,
            windows: true,
            userSid: sid);
        await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var client = await ConnectPipe(sid);
        var snapshot = await ReadStream(client);
        Assert.Equal(FrameTypes.Snapshot, snapshot.Type);
        cts.Cancel();
        await serve;
    }

    [Fact]
    public async Task Public_firewall_profile_is_skipped_and_private_or_domain_is_used()
    {
        var firewall = new FakeFirewallProfile();
        firewall.Set("private-nic", FirewallProfileKind.Private);
        firewall.Set("domain-nic", FirewallProfileKind.Domain);
        firewall.Set("public-nic", FirewallProfileKind.Public);
        var root = Directory.CreateTempSubdirectory("contextall-fw-").FullName;
        using var host = new SessionHost(new LinuxHostPaths(
            identityDirectory: Path.Combine(root, "id"),
            settingsFile: Path.Combine(root, "config.yaml"),
            stateDirectory: Path.Combine(root, "state"),
            transfersDirectory: Path.Combine(root, "transfers"),
            defaultDownloadDirectory: Path.Combine(root, "downloads")), firewall: firewall);

        await host.RebuildNetworkForTest(
        [
            new OnLinkPrefix("10.0.0.2", 24) { InterfaceId = "private-nic" },
            new OnLinkPrefix("10.0.1.2", 24) { InterfaceId = "domain-nic" },
            new OnLinkPrefix("8.8.8.1", 24) { InterfaceId = "public-nic" },
        ]);

        var addresses = host.LastNetworkForTest.Select(prefix => prefix.Address).OrderBy(address => address, StringComparer.Ordinal).ToArray();
        Assert.Equal(["10.0.0.2", "10.0.1.2"], addresses);
    }

    [Fact]
    public void Failed_profile_lookup_skips_the_interface_without_throwing()
    {
        var lookupCalls = 0;
        using var profile = new CachingFirewallProfile(_ =>
        {
            lookupCalls++;
            throw new InvalidOperationException("nlm-missing");
        });

        var thrown = Record.Exception(() => profile.ProfileFor("adapter-1"));
        Assert.Null(thrown);
        Assert.Equal(FirewallProfileKind.Unknown, profile.ProfileFor("adapter-1"));
        Assert.Equal(1, lookupCalls);

        var filtered = FirewallProfiles.Filter(
            [new OnLinkPrefix("10.0.0.8", 24) { InterfaceId = "adapter-1" }],
            profile).ToArray();
        Assert.Empty(filtered);
    }

    [Fact]
    public void Cached_profile_refreshes_on_network_change()
    {
        var kind = FirewallProfileKind.Private;
        using var profile = new CachingFirewallProfile(_ => kind);
        Assert.Equal(FirewallProfileKind.Private, profile.ProfileFor("nic"));
        kind = FirewallProfileKind.Public;
        Assert.Equal(FirewallProfileKind.Private, profile.ProfileFor("nic"));
        var raised = 0;
        profile.Changed += (_, _) => raised++;
        profile.Invalidate();
        Assert.Equal(1, raised);
        Assert.Equal(FirewallProfileKind.Public, profile.ProfileFor("nic"));
    }

    [Fact]
    public void Firewall_script_declares_private_inbound_udp_rules()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "packaging/windows/install-firewall.ps1"));
        Assert.Contains("42461", script, StringComparison.Ordinal);
        Assert.Contains("42462", script, StringComparison.Ordinal);
        Assert.Contains("Private", script, StringComparison.Ordinal);
        Assert.Contains("Inbound", script, StringComparison.Ordinal);
        Assert.Contains("UDP", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Firewall_script_parses_and_dry_run_lists_both_ports()
    {
        var path = Path.Combine(RepoRoot(), "packaging/windows/install-firewall.ps1");
        var shell = ResolvePowerShell();
        if (shell is null)
        {
            var script = File.ReadAllText(path);
            Assert.Contains("42461", script, StringComparison.Ordinal);
            Assert.Contains("42462", script, StringComparison.Ordinal);
            return;
        }

        var parse = Run(shell, ["-NoProfile", "-NonInteractive", "-Command",
            "$null = [System.Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''") + "', [ref]$null, [ref]$errs); if ($errs) { $errs | ForEach-Object { $_.ToString() }; exit 1 }"]);
        Assert.Equal(0, parse.Code);

        var dryRun = Run(shell, ["-NoProfile", "-NonInteractive", "-File", path, "-WhatIf"]);
        Assert.Contains("42461", dryRun.Text, StringComparison.Ordinal);
        Assert.Contains("42462", dryRun.Text, StringComparison.Ordinal);
        Assert.Contains("Private", dryRun.Text, StringComparison.Ordinal);
    }

    static async Task<NamedPipeClientStream> ConnectPipe(string sid)
    {
        var client = new NamedPipeClientStream(".", DaemonProcess.PipeName(sid), PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        return client;
    }

    static async Task<Frame> ReadStream(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ContextAll.Linux.slnf")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repo");
    }

    static string? ResolvePowerShell()
    {
        foreach (var name in new[] { "pwsh", "powershell" })
        {
            try
            {
                var probe = Process.Start(new ProcessStartInfo(name, "-NoProfile -NonInteractive -Command exit 0")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (probe is null)
                {
                    continue;
                }

                probe.WaitForExit(3000);
                if (probe.ExitCode == 0)
                {
                    return name;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
            }
        }

        return null;
    }

    static (int Code, string Text) Run(string fileName, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException(fileName);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        return (process.ExitCode, output + error);
    }

    sealed class FakeFirewallProfile : IFirewallProfile
    {
        readonly Dictionary<string, FirewallProfileKind> map = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler? Changed;

        public void Set(string interfaceId, FirewallProfileKind kind) => map[interfaceId] = kind;

        public FirewallProfileKind ProfileFor(string interfaceId) =>
            map.TryGetValue(interfaceId, out var kind) ? kind : FirewallProfileKind.Unknown;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
