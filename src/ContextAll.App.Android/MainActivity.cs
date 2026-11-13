using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using ContextAll.App.Android.Diagnostics;
using ContextAll.Protocol;

[assembly: Application(Debuggable = true)]

namespace ContextAll.App.Android;

[Activity(Name = "app.contextall.MainActivity", Label = "ContextAll", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    readonly object uiGate = new();
    readonly Dictionary<string, DeviceEventPayload> observedDevices = [];
    readonly Dictionary<string, PairingSnapshotPayload> observedPrompts = [];
    ArrayAdapter<string>? listAdapter;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var devicesView = new ListView(this);
        listAdapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, new List<string> { "Devices" });
        devicesView.Adapter = listAdapter;
        SetContentView(devicesView);

        _ = AndroidSession.Run();
        AndroidSession.Published += OnSessionPublished;

        using var serviceIntent = new Intent(this, typeof(LinkService));
        StartForegroundService(serviceIntent);

        HandleIntent(Intent);
    }

    protected override void OnDestroy()
    {
        AndroidSession.Published -= OnSessionPublished;
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIntent(intent);
    }

    public void ResetObservedEvents()
    {
        lock (uiGate)
        {
            observedDevices.Clear();
            observedPrompts.Clear();
        }
    }

    public PairingSnapshotPayload? LatestPairingPromptFor(string deviceId)
    {
        lock (uiGate)
        {
            return observedPrompts.TryGetValue(deviceId, out var prompt) ? prompt : null;
        }
    }

    public bool HasObservedDeviceState(string deviceId, bool trusted, string presence)
    {
        lock (uiGate)
        {
            return observedDevices.TryGetValue(deviceId, out var dev)
                && dev.Trusted == trusted
                && string.Equals(dev.Presence, presence, StringComparison.Ordinal);
        }
    }

    void OnSessionPublished(IReadOnlyList<Frame> frames)
    {
        foreach (var frame in frames)
        {
            if (frame.Type == FrameTypes.PairingPrompt)
            {
                var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PairingSnapshotPayload);
                if (!decoded.IsFailure && decoded.Value is { } prompt)
                {
                    RunOnUiThread(() =>
                    {
                        lock (uiGate)
                        {
                            observedPrompts[prompt.DeviceId] = prompt;
                        }

                        RefreshUiList();
                    });
                }
            }
            else if (frame.Type == FrameTypes.DeviceChanged)
            {
                var decoded = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.DeviceEventPayload);
                if (!decoded.IsFailure && decoded.Value is { } dev)
                {
                    RunOnUiThread(() =>
                    {
                        lock (uiGate)
                        {
                            observedDevices[dev.DeviceId] = dev;
                        }

                        RefreshUiList();
                    });
                }
            }
        }
    }

    void RefreshUiList()
    {
        var adapter = listAdapter;
        if (adapter is null)
        {
            return;
        }

        List<string> rows = ["Devices"];
        lock (uiGate)
        {
            foreach (var prompt in observedPrompts.Values)
            {
                rows.Add($"Pair {prompt.DisplayName} [{prompt.Code}]");
            }

            foreach (var dev in observedDevices.Values)
            {
                rows.Add($"{dev.DisplayName} ({dev.Presence}, trusted={dev.Trusted})");
            }
        }

        adapter.Clear();
        foreach (var row in rows)
        {
            adapter.Add(row);
        }

        adapter.NotifyDataSetChanged();
    }

    void HandleIntent(Intent? intent)
    {
        var action = intent?.GetStringExtra("action");
        if (string.Equals(action, "quic_spike", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(() => QuicSpikeRunner.RunAsync(this));
        }
        else if (string.Equals(action, "instrumented_tests", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(() => AndroidInstrumentedTestSuite.RunAllAsync(this));
        }
        else if (string.Equals(action, "live_desktop_integration", StringComparison.OrdinalIgnoreCase))
        {
            var hostIp = intent?.GetStringExtra("host_ip") ?? "10.0.2.2";
            var hostPort = intent?.GetIntExtra("host_port", 42461) ?? 42461;
            var hostId = intent?.GetStringExtra("host_id") ?? "";
            if (!string.IsNullOrWhiteSpace(hostId))
            {
                _ = Task.Run(() => AndroidInstrumentedTestSuite.RunLiveDesktopIntegrationAsync(this, hostIp, hostPort, hostId));
            }
        }
    }
}
