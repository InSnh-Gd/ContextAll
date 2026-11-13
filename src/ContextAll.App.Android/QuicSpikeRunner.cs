using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Android.Content;
using Android.OS;
using Android.Util;
using ContextAll.App.Android.Transport;
using ContextAll.Core;
using ContextAll.Effects;

namespace ContextAll.App.Android;

public sealed class QuicSpikeResult
{
    public bool Success { get; set; }
    public bool IsSupported { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? ErrorDetails { get; set; }
    public string? LocalEndPoint { get; set; }
    public string? Alpn { get; set; }
    public string? ServerDeviceId { get; set; }
    public string? ClientDeviceId { get; set; }
    public string? SentPayload { get; set; }
    public string? ReceivedPayload { get; set; }
    public long DurationMs { get; set; }
    public string TimestampUtc { get; set; } = string.Empty;
    public int ApiLevel { get; set; }
    public string? Abi { get; set; }
    public string? DeviceModel { get; set; }
}

public static class QuicSpikeRunner
{
    const string LogTag = "CONTEXTALL_SPIKE";
    const string PingPayload = "QUIC_SPIKE_PING";
    const string PongPayload = "QUIC_SPIKE_PONG";

    public static async Task<QuicSpikeResult> RunAsync(Context context)
    {
        AndroidMsQuicBackend.Register();
        var stopwatch = Stopwatch.StartNew();
        var result = new QuicSpikeResult
        {
            TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
            ApiLevel = (int)Build.VERSION.SdkInt,
            Abi = Build.SupportedAbis is { Count: > 0 } abis ? abis[0] : null,
            DeviceModel = Build.Model,
            Alpn = QuicLink.Alpn,
            SentPayload = PingPayload,
        };

        try
        {
            result.Stage = "CheckSupport";
            Log.Info(LogTag, $"Starting QUIC spike on API {result.ApiLevel} ({result.DeviceModel}, {result.Abi}). Checking QuicLink.Supported...");
            result.IsSupported = QuicLink.Supported;
            if (!result.IsSupported)
            {
                result.Success = false;
                result.Error = "QuicLink.Supported is false";
                Log.Error(LogTag, result.Error);
                return Complete(context, result, stopwatch);
            }

            result.Stage = "GenerateCertificates";
            using var serverCert = CreateSelfSignedCertificate();
            using var clientCert = CreateSelfSignedCertificate();
            var serverSpki = CertificateChecks.LeafSpki(serverCert);
            var clientSpki = CertificateChecks.LeafSpki(clientCert);
            if (serverSpki is null || clientSpki is null)
            {
                result.Success = false;
                result.Error = "Failed to extract P-256 SPKI from generated certificates.";
                return Complete(context, result, stopwatch);
            }

            result.ServerDeviceId = DeviceIds.Text(serverSpki);
            result.ClientDeviceId = DeviceIds.Text(clientSpki);

            result.Stage = "StartListener";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var listener = await QuicLink.Backend
                .ListenMutualAsync(IPAddress.Loopback, serverCert, port: 0, cts.Token)
                .ConfigureAwait(false);
            result.LocalEndPoint = listener.LocalEndPoint.ToString();
            Log.Info(LogTag, $"MsQuic listener bound on {result.LocalEndPoint}");

            result.Stage = "ServerAcceptTask";
            var serverTask = Task.Run(async () =>
            {
                await using var serverConnection = await listener.AcceptConnectionAsync(cts.Token).ConfigureAwait(false);
                if (!QuicLink.TryPeerCertificate(serverConnection, out var remoteClientCert)
                    || CertificateChecks.Evaluate(remoteClientCert, null) != CertificateFate.Continue)
                {
                    throw new InvalidOperationException("Server failed to verify client P-256 certificate.");
                }

                await using var serverStream = await QuicLink.AcceptSession(serverConnection, cts.Token).ConfigureAwait(false);
                var received = await ReadExactUtf8Async(serverStream, Encoding.UTF8.GetByteCount(PingPayload), cts.Token).ConfigureAwait(false);
                if (received == PingPayload)
                {
                    var responseBytes = Encoding.UTF8.GetBytes(PongPayload);
                    await serverStream.WriteAsync(responseBytes, cts.Token).ConfigureAwait(false);
                    await serverStream.FlushAsync(cts.Token).ConfigureAwait(false);
                }

                return received;
            }, cts.Token);

            result.Stage = "ClientConnect";
            var (clientConnection, remoteServerCert) = await QuicLink.Backend
                .ConnectMutualAsync(listener.LocalEndPoint, clientCert, cts.Token)
                .ConfigureAwait(false);
            await using (clientConnection)
            {
                if (CertificateChecks.Evaluate(remoteServerCert, null) != CertificateFate.Continue)
                {
                    throw new InvalidOperationException("Client failed to verify server P-256 certificate.");
                }

                result.Stage = "StreamPingPong";
                await using var clientStream = await QuicLink.OpenSession(clientConnection, cts.Token).ConfigureAwait(false);
                var pingBytes = Encoding.UTF8.GetBytes(PingPayload);
                await clientStream.WriteAsync(pingBytes, cts.Token).ConfigureAwait(false);
                await clientStream.FlushAsync(cts.Token).ConfigureAwait(false);

                var pongReceived = await ReadExactUtf8Async(clientStream, Encoding.UTF8.GetByteCount(PongPayload), cts.Token).ConfigureAwait(false);
                result.ReceivedPayload = pongReceived;
                var serverReceived = await serverTask.ConfigureAwait(false);

                if (serverReceived == PingPayload && pongReceived == PongPayload)
                {
                    result.Success = true;
                    result.Stage = "Completed";
                    Log.Info(LogTag, $"QUIC mutual TLS 1.3 handshake and stream ping-pong succeeded on {result.LocalEndPoint}");
                }
                else
                {
                    result.Success = false;
                    result.Error = $"Payload mismatch: server='{serverReceived}', client='{pongReceived}'";
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.ErrorDetails = ex.ToString();
            Log.Error(LogTag, $"QUIC spike failed at stage '{result.Stage}': {ex}");
        }

        return Complete(context, result, stopwatch);
    }

    static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (key is null)
        {
            throw new InvalidOperationException("P-256 unavailable.");
        }

        var req = new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256);
        using var issued = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var pfx = issued.Export(X509ContentType.Pfx, "contextall");
        return X509CertificateLoader.LoadPkcs12(pfx, "contextall", X509KeyStorageFlags.Exportable);
    }

    static async Task<string> ReadExactUtf8Async(Stream stream, int targetBytes, CancellationToken ct)
    {
        var buffer = new byte[targetBytes];
        var total = 0;
        while (total < targetBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, targetBytes - total), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    static QuicSpikeResult Complete(Context context, QuicSpikeResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        result.DurationMs = stopwatch.ElapsedMilliseconds;
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Log.Info(LogTag, $"RESULT={(result.Success ? "PASS" : "FAIL")}, IsSupported={result.IsSupported}, Stage={result.Stage}, DurationMs={result.DurationMs}, Error={result.Error}");

        try
        {
            var filesDir = context.FilesDir?.AbsolutePath ?? "/data/user/0/app.contextall/files";
            Directory.CreateDirectory(filesDir);
            File.WriteAllText(Path.Combine(filesDir, "quic_spike_result.json"), json);

            var extFiles = context.GetExternalFilesDir(null)?.AbsolutePath;
            if (extFiles is not null)
            {
                Directory.CreateDirectory(extFiles);
                File.WriteAllText(Path.Combine(extFiles, "quic_spike_result.json"), json);
            }
        }
        catch (Exception writeEx)
        {
            Log.Error(LogTag, $"Failed to write result file: {writeEx.Message}");
        }

        return result;
    }
}
