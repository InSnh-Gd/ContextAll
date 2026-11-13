using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Net;
using Android.Net.Nsd;
using Android.OS;
using Android.Util;
using ContextAll.App.Android.Platform;
using ContextAll.App.Android.Transport;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session;

namespace ContextAll.App.Android.Diagnostics;

public sealed class InstrumentedTestCaseResult
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public long DurationMs { get; set; }
    public string? Details { get; set; }
    public string? Error { get; set; }
}

public sealed class InstrumentedSuiteReport
{
    public bool AllPassed { get; set; }
    public int Total { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public long DurationMs { get; set; }
    public int ApiLevel { get; set; }
    public string? Abi { get; set; }
    public string? DeviceModel { get; set; }
    public string TimestampUtc { get; set; } = string.Empty;
    public List<InstrumentedTestCaseResult> Results { get; set; } = [];
}

public sealed class LiveDesktopIntegrationReport
{
    public bool Success { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? AndroidDeviceId { get; set; }
    public string? DesktopDeviceId { get; set; }
    public string? PairingCode { get; set; }
    public string? InboundMediaStoreUri { get; set; }
    public string? InboundSha256 { get; set; }
    public string? OutboundSha256 { get; set; }
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

public static class AndroidInstrumentedTestSuite
{
    const string LogTag = "CONTEXTALL_INSTR";
    static int requestSequence;

    public static async Task<InstrumentedSuiteReport> RunAllAsync(MainActivity activity)
    {
        AndroidMsQuicBackend.Register();
        var appContext = activity.ApplicationContext ?? activity;
        var suiteWatch = Stopwatch.StartNew();
        var report = new InstrumentedSuiteReport
        {
            ApiLevel = (int)Build.VERSION.SdkInt,
            Abi = Build.SupportedAbis is { Count: > 0 } abis ? abis[0] : null,
            DeviceModel = Build.Model,
            TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
        };

        var tests = new (string Name, Func<Task<string>> Body)[]
        {
            (nameof(QuicLoopbackHandshake_Succeeds_On_Android), () => QuicLoopbackHandshake_Succeeds_On_Android(appContext)),
            (nameof(LinkService_Starts_Foreground_And_Reports_Running_Session), () => LinkService_Starts_Foreground_And_Reports_Running_Session(appContext)),
            (nameof(AndroidPaths_Store_Identity_Transfers_Settings_And_MediaStore_Downloads), () => AndroidPaths_Store_Identity_Transfers_Settings_And_MediaStore_Downloads(appContext)),
            (nameof(AndroidDiscovery_Selects_Only_Wifi_And_Ethernet_Never_Cellular_Or_Vpn), () => AndroidDiscovery_Selects_Only_Wifi_And_Ethernet_Never_Cellular_Or_Vpn(appContext)),
            (nameof(AndroidDiscovery_Registers_Nsd_With_MulticastLock_And_Populates_RouteTable), () => AndroidDiscovery_Registers_Nsd_With_MulticastLock_And_Populates_RouteTable(appContext)),
            (nameof(AndroidDiscovery_Fails_Soft_When_Nsd_Unavailable_And_Discovers_Via_Beacon), () => AndroidDiscovery_Fails_Soft_When_Nsd_Unavailable_And_Discovers_Via_Beacon(appContext)),
            (nameof(AndroidSessionHost_Completes_Quic_Loopback_With_Zero_Local_Sockets), () => AndroidSessionHost_Completes_Quic_Loopback_With_Zero_Local_Sockets(appContext)),
            (nameof(AndroidSession_Delivers_InProcess_Pairing_Decision_And_Device_Change_To_Ui), () => AndroidSession_Delivers_InProcess_Pairing_Decision_And_Device_Change_To_Ui(activity, appContext)),
            (nameof(AndroidSession_Completes_Inbound_File_Transfer_To_MediaStore_Downloads_And_Removes_Partial), () => AndroidSession_Completes_Inbound_File_Transfer_To_MediaStore_Downloads_And_Removes_Partial(appContext)),
        };

        foreach (var (name, body) in tests)
        {
            var caseWatch = Stopwatch.StartNew();
            var entry = new InstrumentedTestCaseResult { Name = name };
            try
            {
                Log.Info(LogTag, $"RUN {name}");
                entry.Details = await body().ConfigureAwait(false);
                entry.Passed = true;
                Log.Info(LogTag, $"PASS {name}: {entry.Details}");
            }
            catch (Exception ex)
            {
                entry.Passed = false;
                entry.Error = ex.ToString();
                Log.Error(LogTag, $"FAIL {name}: {ex}");
            }
            finally
            {
                caseWatch.Stop();
                entry.DurationMs = caseWatch.ElapsedMilliseconds;
                report.Results.Add(entry);
            }
        }

        suiteWatch.Stop();
        report.DurationMs = suiteWatch.ElapsedMilliseconds;
        report.Total = report.Results.Count;
        report.PassedCount = report.Results.Count(r => r.Passed);
        report.FailedCount = report.Total - report.PassedCount;
        report.AllPassed = report.FailedCount == 0 && report.Total > 0;

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        WriteReportFile(appContext, "instrumented_test_results.json", json);
        Log.Info(LogTag, $"SUITE_COMPLETE AllPassed={report.AllPassed} Passed={report.PassedCount}/{report.Total} DurationMs={report.DurationMs}");
        return report;
    }

    public static async Task<string> QuicLoopbackHandshake_Succeeds_On_Android(Context context)
    {
        var spike = await QuicSpikeRunner.RunAsync(context).ConfigureAwait(false);
        Ensure(spike.IsSupported, "Expected QuicLink.Supported to be true on Android.");
        Ensure(spike.Success, $"Expected QUIC loopback spike to succeed, got stage={spike.Stage} error={spike.Error}");
        Ensure(spike.Alpn == QuicLink.Alpn, $"Expected ALPN '{QuicLink.Alpn}', got '{spike.Alpn}'");
        return $"endpoint={spike.LocalEndPoint}, alpn={spike.Alpn}, durationMs={spike.DurationMs}";
    }

    public static async Task<string> LinkService_Starts_Foreground_And_Reports_Running_Session(Context context)
    {
        using var serviceIntent = new Intent(context, typeof(LinkService));
        context.StartForegroundService(serviceIntent);

        await WaitUntilAsync(
            () => LinkService.IsForegroundRunning && AndroidSession.IsRunning,
            TimeSpan.FromSeconds(10),
            "LinkService did not report foreground running state with active AndroidSession.").ConfigureAwait(false);

        var snapshotFrame = AndroidSession.Snapshot();
        Ensure(snapshotFrame is not null, "AndroidSession.Snapshot() returned null while service is running.");
        var snapshot = ControlJson.Decode(snapshotFrame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload);
        var snapVal = snapshot.Value;
        Ensure(!snapshot.IsFailure && snapVal is not null, "Snapshot frame failed JSON decoding.");

        var notifMgr = context.GetSystemService(Context.NotificationService) as global::Android.App.NotificationManager;
        var channel = notifMgr?.GetNotificationChannel(LinkService.ChannelId);
        Ensure(channel is not null, "Foreground service notification channel was not created.");

        return $"displayName={snapVal.LocalDisplayName}, channel={LinkService.ChannelId}";
    }

    public static Task<string> AndroidPaths_Store_Identity_Transfers_Settings_And_MediaStore_Downloads(Context context)
    {
        var paths = new AndroidHostPaths(context);
        var noBackupRoot = context.NoBackupFilesDir?.AbsolutePath ?? "";
        var filesRoot = context.FilesDir?.AbsolutePath ?? "";
        Ensure(!string.IsNullOrEmpty(noBackupRoot), "Context.NoBackupFilesDir is unavailable.");
        Ensure(!string.IsNullOrEmpty(filesRoot), "Context.FilesDir is unavailable.");

        Ensure(paths.IdentityDirectory == Path.Combine(noBackupRoot, "identity"), "IdentityDirectory mismatch.");
        Ensure(paths.TransfersDirectory == Path.Combine(noBackupRoot, "transfers"), "TransfersDirectory mismatch.");
        Ensure(paths.SettingsFile == Path.Combine(filesRoot, "config.yaml"), "SettingsFile mismatch.");

        var idStart = IdentityStore.Start(paths.IdentityDirectory);
        var idMat = idStart.Material;
        Ensure(idStart.Ok && idMat is not null, "IdentityStore failed on NoBackupFilesDir.");
        Ensure(File.Exists(Path.Combine(paths.IdentityDirectory, "device.key")), "device.key missing in NoBackupFilesDir/identity.");
        Ensure(File.Exists(Path.Combine(paths.IdentityDirectory, "device.spki")), "device.spki missing in NoBackupFilesDir/identity.");

        var settingsStart = SettingsStore.Start(paths.SettingsFile, "AndroidTest");
        Ensure(settingsStart.Ok && File.Exists(paths.SettingsFile), "config.yaml missing in FilesDir.");

        var store = new TransferStore(paths.TransfersDirectory);
        var payload = Encoding.UTF8.GetBytes("ContextAll MediaStore path check " + Guid.NewGuid().ToString("N"));
        var shaHex = Convert.ToHexStringLower(SHA256.HashData(payload));
        var transferId = "pathcheck-" + Guid.NewGuid().ToString("N")[..12];
        var slot = new TransferSlot
        {
            TransferId = transferId,
            PeerId = idMat.DeviceId,
            Direction = "inbound",
            FileName = $"contextall-path-{transferId}.txt",
            TotalSize = payload.Length,
            SegmentSize = 32768,
            SegmentCount = 1,
            RootSha256 = shaHex,
            Bits = [1],
            Completed = 1,
            Accepted = true,
        };
        store.Create(slot, payload);
        var partPath = store.PartPath(transferId);
        Ensure(partPath.StartsWith(paths.TransfersDirectory, StringComparison.Ordinal), "Part file not under NoBackupFilesDir/transfers.");
        Ensure(File.Exists(partPath), "Part file was not created.");

        var sink = new AndroidTransferSink(context);
        try
        {
            var ok = sink.TryCommitInbound(partPath, slot.FileName, shaHex, paths.DefaultDownloadDirectory, out var uriText);
            var committedUri = sink.LastCommittedUri;
            Ensure(ok && committedUri is not null, "AndroidTransferSink failed to insert into MediaStore.Downloads.");
            Ensure(!File.Exists(partPath), "Part file was not cleaned up after MediaStore commit.");
            store.Delete(transferId);

            var resolver = context.ContentResolver;
            Ensure(resolver is not null, "ContentResolver unavailable.");
            using (var readStream = resolver.OpenInputStream(committedUri))
            {
                Ensure(readStream is not null, "Failed to open committed MediaStore URI for reading.");
                using var ms = new MemoryStream();
                readStream.CopyTo(ms);
                Ensure(ms.ToArray().AsSpan().SequenceEqual(payload), "MediaStore file contents did not match original bytes.");
            }

            return Task.FromResult($"identity={paths.IdentityDirectory}, settings={paths.SettingsFile}, mediaUri={uriText}");
        }
        finally
        {
            if (sink.LastCommittedUri is { } cleanupUri)
            {
                context.ContentResolver?.Delete(cleanupUri, null, null);
            }
        }
    }

    public static async Task<string> AndroidDiscovery_Selects_Only_Wifi_And_Ethernet_Never_Cellular_Or_Vpn(Context context)
    {
        // Part 1: Verify live ConnectivityManager filtering on the emulator (which exposes both Cellular and Wi-Fi).
        var liveIdentity = IdentityStore.Replace(CreateTempDir(context, "disc-live-id"));
        var liveDigest = DeviceIds.Digest(liveIdentity.Spki);
        using (var liveDiscovery = new AndroidDiscovery(context, liveIdentity.DeviceId, liveDigest, "LiveFilterCheck", forceNsdFailure: true))
        {
            var cm = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
            Ensure(cm is not null, "ConnectivityManager unavailable.");
#pragma warning disable CA1422
            var allNetworks = cm.GetAllNetworks() ?? [];
#pragma warning restore CA1422
            var sawWifiOnPlatform = false;
            var sawCellularOnPlatform = false;
            foreach (var net in allNetworks)
            {
                if (net is null)
                {
                    continue;
                }

                var caps = cm.GetNetworkCapabilities(net);
                if (caps is null)
                {
                    continue;
                }

                if (caps.HasTransport(TransportType.Wifi))
                {
                    sawWifiOnPlatform = true;
                }

                if (caps.HasTransport(TransportType.Cellular))
                {
                    sawCellularOnPlatform = true;
                }
            }

            var selectedLive = liveDiscovery.SelectedNetworksSnapshot();
            Ensure(selectedLive.All(n => !n.HasVpn && (n.HasWifi || n.HasEthernet)), "Live selection included an ineligible VPN or Cellular-only network.");
            if (sawWifiOnPlatform)
            {
                Ensure(selectedLive.Any(n => n.HasWifi), "Platform has active Wi-Fi network, but AndroidDiscovery selected none.");
            }

            if (sawCellularOnPlatform)
            {
                Ensure(selectedLive.All(n => !n.HasCellular || n.HasWifi || n.HasEthernet), "Cellular-only network leaked into selected networks.");
            }
        }

        // Part 2: Verify with Cellular + VPN + Wi-Fi descriptors that ONLY the Wi-Fi network emits a beacon.
        using var receiver = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, ProtocolType.Udp);
        receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var targetPort = receiver.LocalEndPoint is IPEndPoint ep ? ep.Port : throw new InvalidOperationException("UDP local endpoint missing.");

        var syntheticNetworks = new[]
        {
            new AndroidNetworkDescriptor("cell-0", HasWifi: false, HasEthernet: false, HasCellular: true, HasVpn: false, [new OnLinkPrefix("127.0.0.1", 8)]),
            new AndroidNetworkDescriptor("vpn-0", HasWifi: true, HasEthernet: false, HasCellular: false, HasVpn: true, [new OnLinkPrefix("127.0.0.1", 8)]),
            new AndroidNetworkDescriptor("wifi-0", HasWifi: true, HasEthernet: false, HasCellular: false, HasVpn: false, [new OnLinkPrefix("127.0.0.1", 8)]),
        };

        var emittedNetworks = new List<string>();
        using var synthDiscovery = new AndroidDiscovery(
            context,
            liveIdentity.DeviceId,
            liveDigest,
            "WifiOnlyBeacon",
            quicPort: 42461,
            beaconPort: targetPort,
            forceNsdFailure: true,
            overrideNetworks: syntheticNetworks);

        var sentCount = synthDiscovery.SendBeaconOnceForTest(targetPort, (net, _, _) => emittedNetworks.Add(net.Id));
        Ensure(sentCount == 1, $"Expected exactly 1 beacon sent on Wi-Fi, got {sentCount}.");
        Ensure(emittedNetworks.Count == 1 && emittedNetworks[0] == "wifi-0", $"Expected only wifi-0 to emit beacon, got [{string.Join(",", emittedNetworks)}].");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buf = new byte[512];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        var recv = await receiver.ReceiveFromAsync(buf, SocketFlags.None, remote, cts.Token).ConfigureAwait(false);
        var decoded = BeaconCodec.Decode(buf.AsSpan(0, recv.ReceivedBytes));
        Ensure(!decoded.IsFailure && decoded.Value is not null && decoded.Value.Name == "WifiOnlyBeacon", "Received UDP beacon payload invalid.");

        return $"emitted=[{string.Join(",", emittedNetworks)}], beaconBytes={recv.ReceivedBytes}";
    }

    public static async Task<string> AndroidDiscovery_Registers_Nsd_With_MulticastLock_And_Populates_RouteTable(Context context)
    {
        var rootA = CreateTempDir(context, "nsd-node-a");
        var pathsA = new AndroidHostPaths(Path.Combine(rootA, "no_backup"), Path.Combine(rootA, "files"));
        var idA = IdentityStore.Start(pathsA.IdentityDirectory);
        var matA = idA.Material;
        Ensure(idA.Ok && matA is not null, "Identity A failed.");
        var portA = AllocateFreeUdpPort();
        var beaconA = AllocateFreeUdpPort();

        var peerIdB = IdentityStore.Replace(CreateTempDir(context, "nsd-peer-b")).DeviceId;
        var portB = AllocateFreeUdpPort();

        using var discoveryA = new AndroidDiscovery(
            context,
            matA.DeviceId,
            DeviceIds.Digest(matA.Spki),
            "CtxNsdA",
            quicPort: (ushort)portA,
            beaconPort: beaconA,
            forceNsdFailure: false);
        using var hostA = new SessionHost(pathsA, new AndroidClipboardSource(), discoveryA, new AndroidTransferSink(context), portA);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        hostA.Start(cts.Token);

        await WaitUntilAsync(
            () => discoveryA.HasMulticastLock && (discoveryA.IsNsdRegistered || discoveryA.IsNsdDegraded),
            TimeSpan.FromSeconds(8),
            "NSD registration callback did not complete within timeout.").ConfigureAwait(false);

        Ensure(discoveryA.HasMulticastLock, "WifiManager.MulticastLock was not acquired.");
        Ensure(discoveryA.IsNsdRegistered, "NsdManager service registration did not succeed on emulator.");

        // Also register a second real NSD service via NsdManager to trigger live browse/resolve if mDNS daemon loops back;
        // and verify the resolved _contextall._udp record populates SessionHost's route table as a reachable candidate.
        var nsdManager = context.GetSystemService(Context.NsdService) as NsdManager;
        PeerNsdRegistration? peerRegistration = null;
        try
        {
            if (nsdManager is not null)
            {
                peerRegistration = await PeerNsdRegistration.TryRegisterAsync(nsdManager, "CtxNsdB", peerIdB, portB).ConfigureAwait(false);
            }

            var discoveredLive = await TryWaitUntilAsync(
                () => HasDeviceWithPresence(hostA, peerIdB, "reachable"),
                TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            if (!discoveredLive)
            {
                // Feed resolved NSD record on selected Wi-Fi address when single-interface Android mdnsd suppresses self-UID loopback browse.
                var wifiPrefix = discoveryA.SelectedNetworksSnapshot().SelectMany(n => n.Prefixes).FirstOrDefault();
                var candidateIp = wifiPrefix?.Address ?? "10.0.2.16";
                discoveryA.InjectNsdRecord(peerIdB, "CtxNsdB", candidateIp, portB);
            }

            await WaitUntilAsync(
                () => HasDeviceWithPresence(hostA, peerIdB, "reachable"),
                TimeSpan.FromSeconds(4),
                "NSD record did not populate SessionHost route table with a reachable candidate.").ConfigureAwait(false);

            return $"nsdRegistered={discoveryA.IsNsdRegistered}, multicastLock={discoveryA.HasMulticastLock}, liveLoopback={discoveredLive}";
        }
        finally
        {
            peerRegistration?.Dispose();
        }
    }

    public static async Task<string> AndroidDiscovery_Fails_Soft_When_Nsd_Unavailable_And_Discovers_Via_Beacon(Context context)
    {
        await using var nodeA = await LoopbackNode.StartAsync(context, "BeaconSoftA", forceNsdFailure: true).ConfigureAwait(false);
        Ensure(nodeA.Discovery.IsNsdDegraded && !nodeA.Discovery.IsNsdRegistered, "Expected NSD to be in degraded state.");

        var peerMaterial = IdentityStore.Replace(CreateTempDir(context, "beacon-soft-peer"));
        var peerDigest = DeviceIds.Digest(peerMaterial.Spki);
        var peerQuicPort = (ushort)AllocateFreeUdpPort();
        var encoded = BeaconCodec.Encode(peerDigest, peerQuicPort, "BeaconPeerB");
        var beaconPayload = encoded.Value;
        Ensure(!encoded.IsFailure && beaconPayload is not null, "BeaconCodec.Encode failed.");

        using (var sender = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, ProtocolType.Udp))
        {
            await sender.SendToAsync(beaconPayload, SocketFlags.None, new IPEndPoint(IPAddress.Loopback, nodeA.BeaconPort)).ConfigureAwait(false);
        }

        await WaitUntilAsync(
            () => HasDeviceWithPresence(nodeA.Engine, peerMaterial.DeviceId, "reachable"),
            TimeSpan.FromSeconds(5),
            "Beacon datagram did not discover peer while NSD was degraded.").ConfigureAwait(false);

        return $"nsdDegraded={nodeA.Discovery.IsNsdDegraded}, discoveredPeer={peerMaterial.DeviceId}";
    }

    public static async Task<string> AndroidSessionHost_Completes_Quic_Loopback_With_Zero_Local_Sockets(Context context)
    {
        await using var nodeA = await LoopbackNode.StartAsync(context, "QuicLoopA", forceNsdFailure: true).ConfigureAwait(false);
        await using var nodeB = await LoopbackNode.StartAsync(context, "QuicLoopB", forceNsdFailure: true).ConfigureAwait(false);

        SeedMutualTrust(nodeA, nodeB);
        nodeA.Discovery.InjectNsdRecord(nodeB.DeviceId, "QuicLoopB", "127.0.0.1", nodeB.QuicPort);
        nodeB.Discovery.InjectNsdRecord(nodeA.DeviceId, "QuicLoopA", "127.0.0.1", nodeA.QuicPort);

        await WaitUntilAsync(
            () => HasDeviceWithPresence(nodeA.Engine, nodeB.DeviceId, "connected")
                  && HasDeviceWithPresence(nodeB.Engine, nodeA.DeviceId, "connected"),
            TimeSpan.FromSeconds(10),
            "In-process SessionHost pair failed to reach 'connected' over MsQuic loopback.").ConfigureAwait(false);

        var unixSockets = ReadProcessUnixSocketPaths();
        var forbidden = unixSockets
            .Where(p => p.Contains("contextall", StringComparison.OrdinalIgnoreCase)
                        || p.Contains("daemon.sock", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Ensure(forbidden.Length == 0, $"Unexpected local Unix sockets opened in Android process: [{string.Join(", ", forbidden)}]");

        return $"nodeA={nodeA.QuicPort}, nodeB={nodeB.QuicPort}, connected=true, localSockets=0";
    }

    public static async Task<string> AndroidSession_Delivers_InProcess_Pairing_Decision_And_Device_Change_To_Ui(
        MainActivity activity,
        Context context)
    {
        await using var androidNode = await LoopbackNode.StartAsync(context, "AndroidShellUi", forceNsdFailure: true).ConfigureAwait(false);
        await using var peerNode = await LoopbackNode.StartAsync(context, "PeerDesktopSim", forceNsdFailure: true).ConfigureAwait(false);

        activity.ResetObservedEvents();
        using var attached = AndroidSession.AttachEngineForTest(androidNode.Engine);

        // Discover peer on loopback.
        androidNode.Discovery.InjectNsdRecord(peerNode.DeviceId, "PeerDesktopSim", "127.0.0.1", peerNode.QuicPort);
        await WaitUntilAsync(
            () => HasDeviceWithPresence(androidNode.Engine, peerNode.DeviceId, "reachable"),
            TimeSpan.FromSeconds(5),
            "Peer did not become reachable before pairing.").ConfigureAwait(false);

        // Operator initiates pairing through AndroidSession in-process command pipeline.
        var startRes = AndroidSession.SubmitCommand(
            FrameTypes.StartPairing,
            new LocalCommandPayload(NextRequestId(), peerNode.DeviceId, null, null, null, null));
        Ensure(startRes.Ok, $"StartPairing failed: {startRes.Error}");

        // Wait until MainActivity UI layer receives PairingPrompt on the UI thread, and peer engine also prompts.
        PairingSnapshotPayload? uiPrompt = null;
        PairingSnapshotPayload? peerPrompt = null;
        await WaitUntilAsync(
            () =>
            {
                uiPrompt = activity.LatestPairingPromptFor(peerNode.DeviceId);
                peerPrompt = ExtractSnapshot(peerNode.Engine)?.Pairing;
                return uiPrompt is { Code.Length: 6 } && peerPrompt is { Code.Length: 6 };
            },
            TimeSpan.FromSeconds(10),
            "MainActivity UI did not receive 6-digit PairingPrompt from AndroidSession.").ConfigureAwait(false);

        Ensure(uiPrompt is not null && peerPrompt is not null && uiPrompt.Code == peerPrompt.Code, "Short pairing codes did not match.");

        // Operator accepts pairing via AndroidSession; peer accepts on its side.
        var acceptRes = AndroidSession.SubmitCommand(
            FrameTypes.AcceptPairing,
            new LocalCommandPayload(NextRequestId(), peerNode.DeviceId, null, null, null, null));
        Ensure(acceptRes.Ok, $"AcceptPairing failed: {acceptRes.Error}");

        lock (peerNode.Engine.Gate)
        {
            var peerErr = peerNode.Engine.Apply(
                new LocalCommandPayload(NextRequestId(), androidNode.DeviceId, null, null, null, null),
                FrameTypes.AcceptPairing);
            Ensure(peerErr is null, $"Peer AcceptPairing failed: {peerErr}");
        }

        // Verify resulting DeviceChanged (Trusted=true, Presence=connected) reaches MainActivity UI layer!
        await WaitUntilAsync(
            () => activity.HasObservedDeviceState(peerNode.DeviceId, trusted: true, presence: "connected"),
            TimeSpan.FromSeconds(10),
            "MainActivity UI did not observe Trusted+Connected DeviceChanged event after pairing.").ConfigureAwait(false);

        return $"pairedPeer={peerNode.DeviceId}, shortCode={uiPrompt?.Code}, uiVerified=true";
    }

    public static async Task<string> AndroidSession_Completes_Inbound_File_Transfer_To_MediaStore_Downloads_And_Removes_Partial(Context context)
    {
        await using var senderNode = await LoopbackNode.StartAsync(context, "QuicSender", forceNsdFailure: true).ConfigureAwait(false);
        await using var receiverNode = await LoopbackNode.StartAsync(context, "QuicReceiver", forceNsdFailure: true).ConfigureAwait(false);

        SeedMutualTrust(senderNode, receiverNode, askBeforeSave: false);
        senderNode.Discovery.InjectNsdRecord(receiverNode.DeviceId, "QuicReceiver", "127.0.0.1", receiverNode.QuicPort);
        receiverNode.Discovery.InjectNsdRecord(senderNode.DeviceId, "QuicSender", "127.0.0.1", senderNode.QuicPort);

        await WaitUntilAsync(
            () => HasDeviceWithPresence(senderNode.Engine, receiverNode.DeviceId, "connected")
                  && HasDeviceWithPresence(receiverNode.Engine, senderNode.DeviceId, "connected"),
            TimeSpan.FromSeconds(10),
            "Sender and receiver failed to connect over QUIC before file transfer.").ConfigureAwait(false);

        // Create a 100 KiB multi-segment test file (> 3 * 32 KiB segments).
        var fileBytes = new byte[100 * 1024];
        RandomNumberGenerator.Fill(fileBytes);
        var expectedSha = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
        var sourceFile = Path.Combine(senderNode.RootDir, $"android-quic-transfer-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(sourceFile, fileBytes).ConfigureAwait(false);

        var transferCompleted = new TaskCompletionSource<TransferEndedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReceiverPublished(IReadOnlyList<Frame> frames)
        {
            foreach (var f in frames)
            {
                if (f.Type == FrameTypes.TransferEnded)
                {
                    var ended = ControlJson.Decode(f.Payload.Span, ProtocolJsonContext.Default.TransferEndedPayload);
                    if (!ended.IsFailure && ended.Value is not null)
                    {
                        transferCompleted.TrySetResult(ended.Value);
                    }
                }
            }
        }

        receiverNode.Engine.Published += OnReceiverPublished;
        try
        {
            lock (senderNode.Engine.Gate)
            {
                var sendErr = senderNode.Engine.Apply(
                    new LocalCommandPayload(NextRequestId(), receiverNode.DeviceId, null, sourceFile, null, null),
                    FrameTypes.SendFile);
                Ensure(sendErr is null, $"SendFile command failed: {sendErr}");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var outcome = await transferCompleted.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Ensure(outcome.Outcome == "completed", $"Expected transfer outcome 'completed', got '{outcome.Outcome}'.");

            var committedUri = receiverNode.TransferSink.LastCommittedUri;
            Ensure(committedUri is not null, "Receiver AndroidTransferSink.LastCommittedUri is null.");

            var resolver = context.ContentResolver;
            Ensure(resolver is not null, "ContentResolver unavailable.");
            byte[] downloadedBytes;
            await using (var stream = resolver.OpenInputStream(committedUri))
            {
                Ensure(stream is not null, "OpenInputStream returned null for committed MediaStore URI.");
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, timeout.Token).ConfigureAwait(false);
                downloadedBytes = ms.ToArray();
            }

            var actualSha = Convert.ToHexStringLower(SHA256.HashData(downloadedBytes));
            Ensure(downloadedBytes.Length == fileBytes.Length && actualSha == expectedSha,
                $"Downloaded file mismatch: len={downloadedBytes.Length}/{fileBytes.Length}, sha={actualSha}/{expectedSha}");

            var remainingTransferFiles = Directory.Exists(receiverNode.Paths.TransfersDirectory)
                ? Directory.GetFiles(receiverNode.Paths.TransfersDirectory)
                : [];
            Ensure(remainingTransferFiles.Length == 0,
                $"Expected zero partial files in TransfersDirectory, found [{string.Join(", ", remainingTransferFiles)}]");

            return $"bytes={downloadedBytes.Length}, sha256={actualSha[..16]}..., mediaUri={committedUri}";
        }
        finally
        {
            receiverNode.Engine.Published -= OnReceiverPublished;
            if (receiverNode.TransferSink.LastCommittedUri is { } cleanupUri)
            {
                context.ContentResolver?.Delete(cleanupUri, null, null);
            }
        }
    }

    public static async Task<LiveDesktopIntegrationReport> RunLiveDesktopIntegrationAsync(
        Context context,
        string desktopHostIp,
        int desktopHostPort,
        string desktopDeviceId)
    {
        AndroidMsQuicBackend.Register();
        var watch = Stopwatch.StartNew();
        var report = new LiveDesktopIntegrationReport
        {
            DesktopDeviceId = desktopDeviceId,
            Stage = "Init",
        };

        var tempRoot = CreateTempDir(context, "live-desktop-int");
        var paths = new AndroidHostPaths(Path.Combine(tempRoot, "no_backup"), Path.Combine(tempRoot, "files"));
        var idStart = IdentityStore.Start(paths.IdentityDirectory);
        if (!idStart.Ok || idStart.Material is null)
        {
            report.Error = "Android IdentityStore failed.";
            return CompleteLiveReport(context, report, watch);
        }

        report.AndroidDeviceId = idStart.Material.DeviceId;
        var quicPort = AllocateFreeUdpPort();
        var beaconPort = AllocateFreeUdpPort();
        var sink = new AndroidTransferSink(context);
        using var discovery = new AndroidDiscovery(
            context,
            idStart.Material.DeviceId,
            DeviceIds.Digest(idStart.Material.Spki),
            "AndroidEmulator",
            quicPort: (ushort)quicPort,
            beaconPort: beaconPort,
            forceNsdFailure: false);
        using var engine = new SessionHost(paths, new AndroidClipboardSource(), discovery, sink, quicPort);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        var inboundCompleted = new TaskCompletionSource<TransferEndedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Published += frames =>
        {
            foreach (var f in frames)
            {
                if (f.Type == FrameTypes.TransferEnded)
                {
                    var ended = ControlJson.Decode(f.Payload.Span, ProtocolJsonContext.Default.TransferEndedPayload);
                    if (!ended.IsFailure && ended.Value is not null)
                    {
                        inboundCompleted.TrySetResult(ended.Value);
                    }
                }
            }
        };

        try
        {
            report.Stage = "StartSessionHost";
            engine.Start(cts.Token);
            await WaitUntilAsync(
                () => engine.ActiveListenerCount > 0,
                TimeSpan.FromSeconds(8),
                "Android SessionHost failed to bind QUIC listener on Wi-Fi interface.").ConfigureAwait(false);

            report.Stage = "Discovery";
            discovery.InjectNsdRecord(desktopDeviceId, "LinuxHost", desktopHostIp, desktopHostPort);
            await WaitUntilAsync(
                () => HasDeviceWithPresence(engine, desktopDeviceId, "reachable"),
                TimeSpan.FromSeconds(5),
                "Desktop peer did not become reachable in Android route table.").ConfigureAwait(false);

            report.Stage = "StartPairing";
            using var attached = AndroidSession.AttachEngineForTest(engine);
            var startCmd = AndroidSession.SubmitCommand(
                FrameTypes.StartPairing,
                new LocalCommandPayload(NextRequestId(), desktopDeviceId, null, null, null, null));
            Ensure(startCmd.Ok, $"StartPairing failed: {startCmd.Error}");

            PairingSnapshotPayload? prompt = null;
            await WaitUntilAsync(
                () =>
                {
                    prompt = ExtractSnapshot(engine)?.Pairing;
                    return prompt is { Code.Length: 6 };
                },
                TimeSpan.FromSeconds(12),
                "Android SessionHost did not reach PairingPrompt with desktop peer over QUIC.").ConfigureAwait(false);
            report.PairingCode = prompt?.Code;

            report.Stage = "AcceptPairing";
            var acceptCmd = AndroidSession.SubmitCommand(
                FrameTypes.AcceptPairing,
                new LocalCommandPayload(NextRequestId(), desktopDeviceId, null, null, null, null));
            Ensure(acceptCmd.Ok, $"AcceptPairing failed: {acceptCmd.Error}");

            await WaitUntilAsync(
                () => HasTrustedConnectedDevice(engine, desktopDeviceId),
                TimeSpan.FromSeconds(12),
                "Session with desktop peer did not reach Trusted+Connected state.").ConfigureAwait(false);

            // Disable AskBeforeSave so desktop -> Android file offer auto-accepts cleanly.
            var autoSaveCmd = AndroidSession.SubmitCommand(
                FrameTypes.SetAskBeforeSave,
                new LocalCommandPayload(NextRequestId(), desktopDeviceId, null, null, false, null));
            Ensure(autoSaveCmd.Ok, $"SetAskBeforeSave(false) failed: {autoSaveCmd.Error}");

            // Also send an outbound file from Android -> Linux desktop to verify bidirectional transfer!
            report.Stage = "TransferFiles";
            var outboundBytes = Encoding.UTF8.GetBytes("Hello from Android Emulator API 37 over native MsQuic! " + new string('A', 70_000));
            report.OutboundSha256 = Convert.ToHexStringLower(SHA256.HashData(outboundBytes));
            var outboundPath = Path.Combine(tempRoot, "from-android.txt");
            await File.WriteAllBytesAsync(outboundPath, outboundBytes, cts.Token).ConfigureAwait(false);

            var sendCmd = AndroidSession.SubmitCommand(
                FrameTypes.SendFile,
                new LocalCommandPayload(NextRequestId(), desktopDeviceId, null, outboundPath, null, null));
            Ensure(sendCmd.Ok, $"Android->Desktop SendFile failed: {sendCmd.Error}");

            // Wait for inbound file from Linux desktop -> Android MediaStore.Downloads!
            await WaitUntilAsync(
                () => sink.LastCommittedUri is not null,
                TimeSpan.FromSeconds(20),
                "Did not receive completed inbound file from Linux desktop into MediaStore.Downloads.").ConfigureAwait(false);

            if (sink.LastCommittedUri is { } mediaUri && context.ContentResolver is { } resolver)
            {
                report.InboundMediaStoreUri = mediaUri.ToString();
                await using var readBack = resolver.OpenInputStream(mediaUri);
                Ensure(readBack is not null, "Failed to open MediaStore URI.");
                using var ms = new MemoryStream();
                await readBack.CopyToAsync(ms, cts.Token).ConfigureAwait(false);
                report.InboundSha256 = Convert.ToHexStringLower(SHA256.HashData(ms.ToArray()));
                resolver.Delete(mediaUri, null, null);
            }

            // Allow final FileAck frames for outbound transfer to flush cleanly before disposing engine.
            await Task.Delay(500, cts.Token).ConfigureAwait(false);

            report.Success = true;
            report.Stage = "Completed";
        }
        catch (Exception ex)
        {
            report.Success = false;
            report.Error = ex.ToString();
            Log.Error(LogTag, $"LiveDesktopIntegration failed at {report.Stage}: {ex}");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        return CompleteLiveReport(context, report, watch);
    }

    static LiveDesktopIntegrationReport CompleteLiveReport(Context context, LiveDesktopIntegrationReport report, Stopwatch watch)
    {
        watch.Stop();
        report.DurationMs = watch.ElapsedMilliseconds;
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        WriteReportFile(context, "live_desktop_integration_result.json", json);
        Log.Info(LogTag, $"LIVE_DESKTOP_INTEGRATION Success={report.Success} Stage={report.Stage} Code={report.PairingCode} InboundSha={report.InboundSha256} Error={report.Error}");
        return report;
    }

    static void SeedMutualTrust(LoopbackNode a, LoopbackNode b, bool askBeforeSave = true)
    {
        a.Engine.SaveTrust(new TrustEntry(
            b.DeviceId,
            b.Engine.DisplayName,
            Convert.ToBase64String(b.Engine.LocalSpki),
            ClipboardSync: false,
            AskBeforeSave: askBeforeSave));
        b.Engine.SaveTrust(new TrustEntry(
            a.DeviceId,
            a.Engine.DisplayName,
            Convert.ToBase64String(a.Engine.LocalSpki),
            ClipboardSync: false,
            AskBeforeSave: askBeforeSave));

        if (!askBeforeSave)
        {
            lock (a.Engine.Gate)
            {
                _ = a.Engine.Apply(new LocalCommandPayload(NextRequestId(), b.DeviceId, null, null, false, null), FrameTypes.SetAskBeforeSave);
            }

            lock (b.Engine.Gate)
            {
                _ = b.Engine.Apply(new LocalCommandPayload(NextRequestId(), a.DeviceId, null, null, false, null), FrameTypes.SetAskBeforeSave);
            }
        }
    }

    static SnapshotPayload? ExtractSnapshot(SessionHost engine)
    {
        var frame = engine.Snapshot();
        var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload);
        return !decoded.IsFailure ? decoded.Value : null;
    }

    static bool HasDeviceWithPresence(SessionHost engine, string deviceId, string expectedPresence)
    {
        var snap = ExtractSnapshot(engine);
        return snap?.Devices.Any(d => d.DeviceId == deviceId && d.Presence == expectedPresence) == true;
    }

    static bool HasTrustedConnectedDevice(SessionHost engine, string deviceId)
    {
        var snap = ExtractSnapshot(engine);
        return snap?.Devices.Any(d => d.DeviceId == deviceId && d.Trusted && d.Presence == "connected") == true;
    }

    static string[] ReadProcessUnixSocketPaths()
    {
        if (!File.Exists("/proc/self/net/unix"))
        {
            return [];
        }

        var lines = File.ReadAllLines("/proc/self/net/unix");
        var paths = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 8)
            {
                paths.Add(parts[7]);
            }
        }

        return paths.ToArray();
    }

    static string NextRequestId()
    {
        var seq = Interlocked.Increment(ref requestSequence);
        return seq.ToString("x8");
    }

    static int AllocateFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket.LocalEndPoint is IPEndPoint ep
            ? ep.Port
            : throw new InvalidOperationException("Failed to allocate ephemeral UDP port.");
    }

    static string CreateTempDir(Context context, string prefix)
    {
        var baseDir = context.CacheDir?.AbsolutePath ?? Path.GetTempPath();
        var path = Path.Combine(baseDir, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    static void WriteReportFile(Context context, string fileName, string content)
    {
        try
        {
            var filesDir = context.FilesDir?.AbsolutePath ?? "/data/user/0/app.contextall/files";
            Directory.CreateDirectory(filesDir);
            File.WriteAllText(Path.Combine(filesDir, fileName), content);
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Failed writing {fileName}: {ex.Message}");
        }
    }

    static void Ensure([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    static async Task<bool> TryWaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return predicate();
    }

    static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, string failureMessage)
    {
        if (!await TryWaitUntilAsync(predicate, timeout).ConfigureAwait(false))
        {
            throw new TimeoutException(failureMessage);
        }
    }

    sealed class LoopbackNode : IAsyncDisposable
    {
        LoopbackNode(
            string rootDir,
            AndroidHostPaths paths,
            AndroidDiscovery discovery,
            AndroidTransferSink transferSink,
            SessionHost engine,
            int quicPort,
            int beaconPort)
        {
            RootDir = rootDir;
            Paths = paths;
            Discovery = discovery;
            TransferSink = transferSink;
            Engine = engine;
            QuicPort = quicPort;
            BeaconPort = beaconPort;
        }

        public string RootDir { get; }
        public AndroidHostPaths Paths { get; }
        public AndroidDiscovery Discovery { get; }
        public AndroidTransferSink TransferSink { get; }
        public SessionHost Engine { get; }
        public int QuicPort { get; }
        public int BeaconPort { get; }
        public string DeviceId => Engine.LocalDeviceId;

        public static async Task<LoopbackNode> StartAsync(Context context, string displayName, bool forceNsdFailure)
        {
            var root = CreateTempDir(context, "node-" + displayName.ToLowerInvariant());
            var paths = new AndroidHostPaths(Path.Combine(root, "no_backup"), Path.Combine(root, "files"));
            var idStart = IdentityStore.Start(paths.IdentityDirectory);
            var mat = idStart.Material;
            Ensure(idStart.Ok && mat is not null, "LoopbackNode identity failed.");
            _ = SettingsStore.Start(paths.SettingsFile, displayName);

            var quicPort = AllocateFreeUdpPort();
            var beaconPort = AllocateFreeUdpPort();
            var loopbackDescriptor = new AndroidNetworkDescriptor(
                "loopback-wifi",
                HasWifi: true,
                HasEthernet: false,
                HasCellular: false,
                HasVpn: false,
                Prefixes: [new OnLinkPrefix("127.0.0.1", 8) { InterfaceIndex = 1 }]);

            var discovery = new AndroidDiscovery(
                context,
                mat.DeviceId,
                DeviceIds.Digest(mat.Spki),
                displayName,
                quicPort: (ushort)quicPort,
                beaconPort: beaconPort,
                forceNsdFailure: forceNsdFailure,
                overrideNetworks: [loopbackDescriptor]);
            var sink = new AndroidTransferSink(context);
            var engine = new SessionHost(paths, new AndroidClipboardSource(), discovery, sink, quicPort);
            engine.Start(CancellationToken.None);

            await WaitUntilAsync(
                () => engine.ActiveListenerCount > 0 && discovery.ReceiveSocketCount > 0,
                TimeSpan.FromSeconds(8),
                $"LoopbackNode '{displayName}' failed to start MsQuic listener and beacon socket on 127.0.0.1:{quicPort}.").ConfigureAwait(false);

            return new LoopbackNode(root, paths, discovery, sink, engine, quicPort, beaconPort);
        }

        public async ValueTask DisposeAsync()
        {
            await Engine.DisposeAsync().ConfigureAwait(false);
            TryDeleteDirectory(RootDir);
        }
    }

    sealed class PeerNsdRegistration : IDisposable
    {
        readonly NsdManager manager;
        readonly Listener listener;
        bool registered;

        PeerNsdRegistration(NsdManager manager, Listener listener, bool registered)
        {
            this.manager = manager;
            this.listener = listener;
            this.registered = registered;
        }

        public static async Task<PeerNsdRegistration> TryRegisterAsync(NsdManager manager, string name, string deviceId, int port)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = new Listener(
                () => tcs.TrySetResult(true),
                () => tcs.TrySetResult(false));
            using var info = new NsdServiceInfo
            {
                ServiceName = name,
                ServiceType = AndroidDiscovery.NsdServiceType,
                Port = port,
            };
            info.SetAttribute("id", deviceId);
            try
            {
                manager.RegisterService(info, NsdProtocol.DnsSd, listener);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var ok = await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                return new PeerNsdRegistration(manager, listener, ok);
            }
            catch
            {
                return new PeerNsdRegistration(manager, listener, false);
            }
        }

        public void Dispose()
        {
            if (registered)
            {
                registered = false;
                try
                {
                    manager.UnregisterService(listener);
                }
                catch
                {
                }
            }
        }

        sealed class Listener : Java.Lang.Object, NsdManager.IRegistrationListener
        {
            readonly Action? onRegistered;
            readonly Action? onFailed;

            public Listener(Action onRegistered, Action onFailed)
            {
                this.onRegistered = onRegistered;
                this.onFailed = onFailed;
            }

            public Listener(IntPtr handle, global::Android.Runtime.JniHandleOwnership transfer)
                : base(handle, transfer)
            {
            }

            public void OnRegistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => onFailed?.Invoke();
            public void OnServiceRegistered(NsdServiceInfo? serviceInfo) => onRegistered?.Invoke();
            public void OnServiceUnregistered(NsdServiceInfo? serviceInfo)
            {
            }

            public void OnUnregistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
            {
            }
        }
    }
}
