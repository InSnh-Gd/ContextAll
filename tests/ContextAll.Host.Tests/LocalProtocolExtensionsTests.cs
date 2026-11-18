using System.Net.Sockets;
using System.Text;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Session;
using Xunit;

namespace ContextAll.Host.Tests;

public sealed class LocalProtocolExtensionsTests
{
    [Fact]
    public void Connect_and_push_command_payloads_round_trip_within_64k_frame_limit()
    {
        Assert.True(LocalFrameTypes.IsCommand(FrameTypes.ConnectEndpoint));
        Assert.True(LocalFrameTypes.IsCommand(FrameTypes.PushClipboardText));
        Assert.True(LocalFrameTypes.IsCommand(FrameTypes.ReadClipboardHistory));
        Assert.True(LocalFrameTypes.IsCommand(FrameTypes.ConfigureClipboardHistory));
        Assert.True(LocalFrameTypes.IsCommand(FrameTypes.UseClipboardHistory));
        Assert.False(LocalFrameTypes.IsCommand(FrameTypes.Snapshot));
        Assert.False(LocalFrameTypes.IsCommand(91));
        Assert.Equal(1024 * 1024, FrameLimits.MaxClipboardBytes);

        var connect = new LocalCommandPayload(
            "req-connect-1",
            null,
            null,
            null,
            null,
            null,
            Host: "10.0.0.42",
            Port: 51515,
            Alias: "Linux Workstation");
        var encodedConnect = ControlJson.Encode(connect, ProtocolJsonContext.Default.LocalCommandPayload);
        Assert.False(encodedConnect.IsFailure);
        var connectFrame = FrameCodec.Encode(encodedConnect.Value!, FrameTypes.ConnectEndpoint, 0, 0, FrameTypes.SessionStream);
        Assert.False(connectFrame.IsFailure);
        var decodedConnectFrame = FrameCodec.Decode(connectFrame.Value!).Value!;
        var decodedConnect = ControlJson.Decode(decodedConnectFrame.Payload.Span, ProtocolJsonContext.Default.LocalCommandPayload).Value!;
        Assert.Equal("10.0.0.42", decodedConnect.Host);
        Assert.Equal(51515, decodedConnect.Port);
        Assert.Equal("Linux Workstation", decodedConnect.Alias);

        var sliceBytes = new byte[32_768];
        Random.Shared.NextBytes(sliceBytes);
        var chunked = new LocalCommandPayload(
            "req-push-1",
            null,
            null,
            null,
            null,
            null,
            TotalBytes: 204_800,
            Offset: 32_768,
            ChunkBase64: Convert.ToBase64String(sliceBytes));
        var encodedChunk = ControlJson.Encode(chunked, ProtocolJsonContext.Default.LocalCommandPayload);
        Assert.False(encodedChunk.IsFailure);
        Assert.True(encodedChunk.Value!.Length <= FrameLimits.MaxPayloadBytes);
        var chunkFrame = FrameCodec.Encode(encodedChunk.Value!, FrameTypes.PushClipboardText, 0, 0, FrameTypes.SessionStream);
        Assert.False(chunkFrame.IsFailure);
    }

    [Fact]
    public void Extended_snapshot_and_event_payloads_preserve_backward_compatible_defaults()
    {
        var minimalSnapshot = ControlJson.Decode(
            """
            {
              "localDisplayName": "OldHost",
              "downloadDirectory": "/tmp",
              "devices": [
                {
                  "deviceId": "ca1old",
                  "displayName": "Peer",
                  "trusted": true,
                  "presence": "online",
                  "clipboardSync": true,
                  "askBeforeSave": false
                }
              ],
              "pairing": null,
              "filePrompts": [],
              "transfers": [
                {
                  "transferId": "t1",
                  "deviceId": "ca1old",
                  "direction": "in",
                  "fileName": "a.txt",
                  "completedSegments": 1,
                  "segmentCount": 2,
                  "moveBlocked": false
                }
              ]
            }
            """u8,
            ProtocolJsonContext.Default.SnapshotPayload);
        Assert.False(minimalSnapshot.IsFailure);
        Assert.Equal("", minimalSnapshot.Value!.LocalDeviceId);
        Assert.Equal(0, minimalSnapshot.Value!.QuicPort);
        Assert.Equal("unavailable", minimalSnapshot.Value!.ClipboardBackend);
        Assert.Null(minimalSnapshot.Value!.Devices[0].RemoteClipboardSync);
        Assert.Null(minimalSnapshot.Value!.Devices[0].Endpoint);
        Assert.Equal(0, minimalSnapshot.Value!.Transfers[0].TotalSize);

        var extendedSnapshot = new SnapshotPayload(
            "Desk",
            "",
            [new DeviceSnapshotPayload("ca1peer", "Phone", true, "online", true, true, true, "192.168.1.9:4242")],
            null,
            [],
            [new TransferSnapshotPayload("t2", "ca1peer", "out", "big.bin", 2, 4, false, 16_777_216)],
            LocalDeviceId: "ca1self",
            ResolvedDownloadDirectory: "/home/user/Downloads",
            LocalAddresses: ["192.168.1.10"],
            QuicPort: 4242,
            ClipboardBackend: "wayland");
        var encoded = ControlJson.Encode(extendedSnapshot, ProtocolJsonContext.Default.SnapshotPayload);
        Assert.False(encoded.IsFailure);
        var roundTrip = ControlJson.Decode(encoded.Value!, ProtocolJsonContext.Default.SnapshotPayload);
        Assert.False(roundTrip.IsFailure);
        Assert.Equal("ca1self", roundTrip.Value!.LocalDeviceId);
        Assert.Equal("/home/user/Downloads", roundTrip.Value!.ResolvedDownloadDirectory);
        Assert.Equal(["192.168.1.10"], roundTrip.Value!.LocalAddresses ?? []);
        Assert.Equal(4242, roundTrip.Value!.QuicPort);
        Assert.Equal("wayland", roundTrip.Value!.ClipboardBackend);
        Assert.True(roundTrip.Value!.Devices[0].RemoteClipboardSync);
        Assert.Equal("192.168.1.9:4242", roundTrip.Value!.Devices[0].Endpoint);
        Assert.Equal(16_777_216, roundTrip.Value!.Transfers[0].TotalSize);
    }

    [Fact]
    public async Task LanHost_wires_extended_snapshot_invalid_connect_chunked_clipboard_and_1mib_limit()
    {
        var root = Directory.CreateTempSubdirectory("ca-host-ext-").FullName;
        try
        {
            using var host = LanHost.CreateIsolated(root);
            var snapshotFrame = host.Snapshot();
            var snapshot = ControlJson.Decode(snapshotFrame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value!;
            Assert.Equal(host.Engine.LocalDeviceId, snapshot.LocalDeviceId);
            Assert.Equal(host.Downloads, snapshot.ResolvedDownloadDirectory);
            Assert.Equal("unavailable", snapshot.ClipboardBackend);
            Assert.NotNull(snapshot.LocalAddresses);

            var client = new FrontClient();
            host.Join(client);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var sockPath = Path.Combine(root, "front.sock");
            listener.Bind(new UnixDomainSocketEndPoint(sockPath));
            listener.Listen(1);
            var acceptTask = listener.AcceptAsync(cts.Token);
            using var readerSock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await readerSock.ConnectAsync(new UnixDomainSocketEndPoint(sockPath), cts.Token);
            using var writerSock = await acceptTask;
            using var writerStream = new NetworkStream(writerSock, ownsSocket: true);
            using var readerStream = new NetworkStream(readerSock, ownsSocket: true);
            var pumpTask = client.CopyTo(writerStream, cts.Token);

            host.Command(client, EncodeCommand(FrameTypes.ConnectEndpoint, new LocalCommandPayload("aa01", null, null, null, null, null, Host: "bad host!", Port: 4242)));
            var badHost = await ReadResultAsync(readerStream, cts.Token);
            Assert.Equal("aa01", badHost.RequestId);
            Assert.False(badHost.Ok);
            Assert.Equal("invalid-endpoint", badHost.Error);

            host.Command(client, EncodeCommand(FrameTypes.ConnectEndpoint, new LocalCommandPayload("aa02", null, null, null, null, null, Host: "127.0.0.1", Port: 70000)));
            var badPort = await ReadResultAsync(readerStream, cts.Token);
            Assert.Equal("aa02", badPort.RequestId);
            Assert.False(badPort.Ok);
            Assert.Equal("invalid-endpoint", badPort.Error);

            host.Command(client, EncodeCommand(FrameTypes.PushClipboardText, new LocalCommandPayload("bb01", null, null, null, null, null, Text: "quick-note")));
            var quickResult = await ReadResultAsync(readerStream, cts.Token);
            Assert.True(quickResult.Ok);
            Assert.Equal("quick-note", host.Clipboard.Text);
            Assert.Equal("quick-note", host.Applied);

            var largeText = new string('K', 200 * 1024);
            var utf8 = Encoding.UTF8.GetBytes(largeText);
            var chunkIndex = 0;
            for (var offset = 0; offset < utf8.Length; offset += 32_768)
            {
                var take = Math.Min(32_768, utf8.Length - offset);
                var reqId = $"cc{chunkIndex:x4}";
                chunkIndex++;
                host.Command(client, EncodeCommand(
                    FrameTypes.PushClipboardText,
                    new LocalCommandPayload(
                        reqId,
                        null,
                        null,
                        null,
                        null,
                        null,
                        TotalBytes: utf8.Length,
                        Offset: offset,
                        ChunkBase64: Convert.ToBase64String(utf8, offset, take))));
                var ack = await ReadResultAsync(readerStream, cts.Token);
                Assert.Equal(reqId, ack.RequestId);
                Assert.True(ack.Ok);
            }

            Assert.Equal(largeText, host.Clipboard.Text);
            Assert.Equal(largeText, host.Applied);

            host.Command(client, EncodeCommand(FrameTypes.PushClipboardText, new LocalCommandPayload("dd01", null, null, null, null, null, TotalBytes: FrameLimits.MaxClipboardBytes + 1)));
            var tooLarge = await ReadResultAsync(readerStream, cts.Token);
            Assert.Equal("dd01", tooLarge.RequestId);
            Assert.False(tooLarge.Ok);
            Assert.Equal("too-large", tooLarge.Error);
            Assert.Equal(largeText, host.Clipboard.Text);

            client.Complete();
            await pumpTask;
            host.Leave(client);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LanHost_duplicate_and_invalid_request_ids_match_shared_operator()
    {
        var root = Directory.CreateTempSubdirectory("ca-host-dup-").FullName;
        try
        {
            using var host = LanHost.CreateIsolated(root);
            var client = new FrontClient();
            host.Join(client);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var sockPath = Path.Combine(root, "front.sock");
            listener.Bind(new UnixDomainSocketEndPoint(sockPath));
            listener.Listen(1);
            var acceptTask = listener.AcceptAsync(cts.Token);
            using var readerSock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await readerSock.ConnectAsync(new UnixDomainSocketEndPoint(sockPath), cts.Token);
            using var writerSock = await acceptTask;
            using var writerStream = new NetworkStream(writerSock, ownsSocket: true);
            using var readerStream = new NetworkStream(readerSock, ownsSocket: true);
            var pumpTask = client.CopyTo(writerStream, cts.Token);

            host.Command(client, EncodeCommand(
                FrameTypes.SetDisplayName,
                new LocalCommandPayload("BAD-ID!", null, "Nope", null, null, null)));
            var badId = await ReadResultAsync(readerStream, cts.Token);
            Assert.False(badId.Ok);
            Assert.Equal("invalid", badId.Error);

            host.Command(client, EncodeCommand(
                FrameTypes.SetDisplayName,
                new LocalCommandPayload("abcdef01", null, "Studio Pixel", null, null, null)));
            var ok = await ReadResultAsync(readerStream, cts.Token);
            Assert.True(ok.Ok);

            host.Command(client, EncodeCommand(
                FrameTypes.SetDisplayName,
                new LocalCommandPayload("abcdef01", null, "Other Name", null, null, null)));
            var duplicate = await ReadResultAsync(readerStream, cts.Token);
            Assert.False(duplicate.Ok);
            Assert.Equal("duplicate-request", duplicate.Error);
            Assert.Equal("Studio Pixel", host.DisplayName);

            client.Complete();
            await pumpTask;
            host.Leave(client);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CliApp_connect_parses_endpoints_and_exposes_connect_menu_entry()
    {
        Assert.Contains("Connect endpoint", CliMenu.Actions);
        Assert.True(LocalClient.TryParseEndpoint("192.168.1.50:42461", out var v4Host, out var v4Port));
        Assert.Equal("192.168.1.50", v4Host);
        Assert.Equal(42461, v4Port);
        Assert.True(LocalClient.TryParseEndpoint("[fe80::1]:42461", out var v6Host, out var v6Port));
        Assert.Equal("fe80::1", v6Host);
        Assert.Equal(42461, v6Port);
        Assert.False(LocalClient.TryParseEndpoint("not-an-endpoint", out _, out _));
        Assert.False(LocalClient.TryParseEndpoint("127.0.0.1:0", out _, out _));

        var errInvalid = new StringWriter();
        var invalidExit = await CliApp.Run(
            ["connect", "bad-endpoint"],
            new CliIo { Out = new StringWriter(), Error = errInvalid, IsTerminal = false, RuntimeDirectory = "/nonexistent" });
        Assert.NotEqual(0, invalidExit);
        Assert.Contains("invalid-endpoint", errInvalid.ToString());

        var errNoDaemon = new StringWriter();
        var missingExit = await CliApp.Run(
            ["connect", "127.0.0.1:42461", "Studio"],
            new CliIo { Out = new StringWriter(), Error = errNoDaemon, IsTerminal = false, RuntimeDirectory = "/nonexistent" });
        Assert.NotEqual(0, missingExit);
        Assert.Contains("The daemon is not running.", errNoDaemon.ToString());
    }

    static Frame EncodeCommand(byte type, LocalCommandPayload payload)
    {
        var json = ControlJson.Encode(payload, ProtocolJsonContext.Default.LocalCommandPayload).Value!;
        var bytes = FrameCodec.Encode(json, type, 0, 0, FrameTypes.SessionStream).Value!;
        return FrameCodec.Decode(bytes).Value!;
    }

    static async Task<CommandResultPayload> ReadResultAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("EOF waiting for CommandResult");
            }

            filled += read;
            var outcome = FrameCodec.Read(buffer.AsSpan(0, filled));
            if (outcome.Frame is { Type: FrameTypes.CommandResult } frame)
            {
                return ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.CommandResultPayload).Value!;
            }
        }

        throw new IOException("Oversized frame");
    }
}
