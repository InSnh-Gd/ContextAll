using System.Net;
using System.Net.Sockets;
using ContextAll.Host;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Harness;

public sealed class UdpBeaconListener : IDisposable
{
    public const int DefaultPort = LanHost.BeaconPort; // 42462

    readonly Socket socket;
    readonly List<Beacon> capturedBeacons = [];
    readonly object sync = new();

    public int Port { get; }
    public IReadOnlyList<Beacon> CapturedBeacons
    {
        get
        {
            lock (sync)
            {
                return [.. capturedBeacons];
            }
        }
    }

    public UdpBeaconListener(int port = DefaultPort, IPAddress? bindAddress = null)
    {
        Port = port;
        bindAddress ??= IPAddress.Any;

        socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.EnableBroadcast = true;
        socket.Bind(new IPEndPoint(bindAddress, port));
    }

    public async Task<Beacon?> WaitForBeaconAsync(TimeSpan timeout, Func<Beacon, bool>? predicate = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var buffer = new byte[2048];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, cts.Token).ConfigureAwait(false);
                var decode = BeaconCodec.Decode(buffer.AsSpan(0, result.ReceivedBytes));
                if (!decode.IsFailure && decode.Value is not null)
                {
                    lock (sync)
                    {
                        capturedBeacons.Add(decode.Value);
                    }

                    if (predicate is null || predicate(decode.Value))
                    {
                        return decode.Value;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    public async Task SendBeaconAsync(
        byte[] digest,
        ushort port,
        string displayName,
        IPEndPoint target,
        CancellationToken ct = default)
    {
        var outcome = BeaconCodec.Encode(digest, port, displayName);
        if (outcome.IsFailure || outcome.Value is null)
        {
            throw new InvalidOperationException("Failed to encode beacon: " + outcome.Reason);
        }

        await socket.SendToAsync(outcome.Value, SocketFlags.None, target, ct).ConfigureAwait(false);
    }

    public async Task SendRawDatagramAsync(
        byte[] datagram,
        IPEndPoint target,
        CancellationToken ct = default)
    {
        await socket.SendToAsync(datagram, SocketFlags.None, target, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try { socket.Close(); } catch { }
        socket.Dispose();
    }
}
