using System.Text;
using ContextAll.Protocol;
using Xunit;

namespace ContextAll.Host.Tests;

public sealed class X11ClipboardTests
{
    [Fact]
    public void Captures_external_utf8_selection_and_suppresses_self_echo()
    {
        var api = new FakeX11Api();
        using var x11 = new X11Clipboard(api, startWorker: false);
        var received = new List<string>();
        x11.PlainText += received.Add;

        api.SetOwnerText(200, "你好 X11 世界", [api.Utf8StringAtom, api.StringAtom]);
        PumpUntilIdle(x11, api);
        Assert.Equal(["你好 X11 世界"], received);

        x11.SetText("from-self");
        PumpUntilIdle(x11, api);
        Assert.Equal(api.Window, api.CurrentOwner);
        Assert.Single(received);

        api.RequestSelection(300, api.Utf8StringAtom, 900);
        PumpUntilIdle(x11, api);
        Assert.Equal("from-self", Encoding.UTF8.GetString(api.GetStoredProperty(300, 900).Bytes));
    }

    [Fact]
    public void Ignores_non_text_targets_and_rejects_oversized_payloads()
    {
        var api = new FakeX11Api();
        using var x11 = new X11Clipboard(api, startWorker: false);
        var received = new List<string>();
        x11.PlainText += received.Add;

        api.SetOwnerBytes(201, [0x89, 0x50, 0x4E, 0x47], [api.ImagePngAtom]);
        PumpUntilIdle(x11, api);
        Assert.Empty(received);

        var oversized = new byte[FrameLimits.MaxClipboardBytes + 128];
        Array.Fill(oversized, (byte)'A');
        api.SetOwnerBytes(202, oversized, [api.Utf8StringAtom]);
        PumpUntilIdle(x11, api);
        Assert.Empty(received);
    }

    [Fact]
    public void Reads_and_writes_incr_chunks_up_to_1mib_and_aborts_on_timeout()
    {
        var now = 1_000L;
        var api = new FakeX11Api();
        using var x11 = new X11Clipboard(api, incrTimeout: TimeSpan.FromMilliseconds(500), clockMs: () => now, startWorker: false);
        var received = new List<string>();
        x11.PlainText += received.Add;

        var expectedText = new string('M', 320 * 1024);
        api.SetOwnerIncrText(203, expectedText, chunkSize: 64 * 1024);
        PumpUntilIdle(x11, api);
        Assert.Equal([expectedText], received);

        // Outbound INCR (> 256 KiB): serve requestor 301
        x11.SetText(expectedText);
        PumpUntilIdle(x11, api);
        var outbound = api.PullOutboundIncr(x11, requestor: 301, property: 901, api.Utf8StringAtom);
        Assert.Equal(expectedText, Encoding.UTF8.GetString(outbound));

        // Stalled INCR read should abort and clean up property after timeout
        api.SetOwnerStalledIncr(204, declaredSize: 4096);
        PumpUntilIdle(x11, api);
        now += 600;
        x11.PumpOnceForTest();
        Assert.True(api.PropertyWasDeleted);

        // Late chunk after timeout must be ignored
        api.InjectLateChunk("stale"u8.ToArray());
        x11.PumpOnceForTest();
        Assert.Single(received);
    }

    [Fact]
    public void Cascade_falls_back_gracefully_when_display_or_x11_missing()
    {
        using var none = LinuxClipboardSource.TryOpen(null, null, null);
        Assert.Equal(ClipboardBackendKind.None, none.BackendKind);
        Assert.Equal("unavailable", none.BackendName);
        Assert.False(none.IsSupported);

        // Unreachable X11 display returns unavailable without throwing (even if libX11 absent or display dead)
        using var deadDisplay = LinuxClipboardSource.TryOpen(null, null, ":9999");
        Assert.Equal(ClipboardBackendKind.None, deadDisplay.BackendKind);
        Assert.False(deadDisplay.IsSupported);

        var x11 = new X11Clipboard(new FakeX11Api(), startWorker: false);
        using var x11Source = new LinuxClipboardSource(null, x11);
        Assert.Equal(ClipboardBackendKind.X11, x11Source.BackendKind);
        Assert.Equal("x11", x11Source.BackendName);
        Assert.True(x11Source.IsSupported);
    }

    static void PumpUntilIdle(X11Clipboard clip, FakeX11Api api)
    {
        for (var i = 0; i < 64; i++)
        {
            clip.PumpOnceForTest();
            if (!api.HasPendingEvents)
            {
                clip.PumpOnceForTest();
                if (!api.HasPendingEvents)
                {
                    break;
                }
            }
        }
    }

    sealed class FakeX11Api : IX11Api
    {
        readonly Queue<X11Event> events = new();
        readonly Dictionary<(ulong Win, ulong Prop), X11PropertyData> props = [];

        ulong remoteOwner;
        byte[] remoteBytes = [];
        ulong[] remoteTargets = [];
        Queue<byte[]> remoteIncrChunks = new();
        bool remoteIncr;
        bool incrStreamStarted;
        bool stallAfterStart;

        public ulong Window => 100;
        public ulong ClipboardAtom => 1;
        public ulong TargetsAtom => 2;
        public ulong IncrAtom => 3;
        public ulong Utf8StringAtom => 4;
        public ulong TextPlainUtf8Atom => 5;
        public ulong TextPlainAtom => 6;
        public ulong TextAtom => 7;
        public ulong StringAtom => 31;
        public ulong AtomTypeAtom => 4;
        public ulong PropertyAtom => 50;
        public ulong ImagePngAtom => 60;

        public ulong CurrentOwner { get; private set; }
        public bool PropertyWasDeleted { get; private set; }
        public bool HasPendingEvents => events.Count > 0;

        public void SetOwnerText(ulong owner, string text, ulong[] targets) =>
            SetOwnerBytes(owner, Encoding.UTF8.GetBytes(text), targets);

        public void SetOwnerBytes(ulong owner, byte[] bytes, ulong[] targets)
        {
            remoteOwner = owner;
            CurrentOwner = owner;
            remoteBytes = bytes;
            remoteTargets = targets;
            remoteIncr = false;
            incrStreamStarted = false;
            remoteIncrChunks.Clear();
            stallAfterStart = false;
            events.Enqueue(new X11SelectionOwnerChanged(ClipboardAtom, owner, 10));
        }

        public void SetOwnerIncrText(ulong owner, string text, int chunkSize)
        {
            var utf8 = Encoding.UTF8.GetBytes(text);
            SetOwnerBytes(owner, utf8, [Utf8StringAtom]);
            remoteIncr = true;
            for (var i = 0; i < utf8.Length; i += chunkSize)
            {
                remoteIncrChunks.Enqueue(utf8.AsSpan(i, Math.Min(chunkSize, utf8.Length - i)).ToArray());
            }
            remoteIncrChunks.Enqueue([]);
        }

        public void SetOwnerStalledIncr(ulong owner, int declaredSize)
        {
            SetOwnerBytes(owner, BitConverter.GetBytes(declaredSize), [Utf8StringAtom]);
            remoteIncr = true;
            stallAfterStart = true;
            PropertyWasDeleted = false;
        }

        public void InjectLateChunk(byte[] bytes)
        {
            props[(Window, PropertyAtom)] = new X11PropertyData(true, Utf8StringAtom, 8, bytes, [], 0);
            events.Enqueue(new X11PropertyNotify(Window, PropertyAtom, 20, Deleted: false));
        }

        public void RequestSelection(ulong requestor, ulong target, ulong prop) =>
            events.Enqueue(new X11SelectionRequest(Window, requestor, ClipboardAtom, target, prop, 30));

        public byte[] PullOutboundIncr(X11Clipboard clip, ulong requestor, ulong property, ulong target)
        {
            RequestSelection(requestor, target, property);
            PumpUntilIdle(clip, this);
            var start = GetStoredProperty(requestor, property);
            Assert.Equal(IncrAtom, start.ActualType);

            var all = new List<byte>();
            for (var i = 0; i < 64; i++)
            {
                props.Remove((requestor, property));
                events.Enqueue(new X11PropertyNotify(requestor, property, (ulong)(40 + i), Deleted: true));
                PumpUntilIdle(clip, this);
                var slice = GetStoredProperty(requestor, property);
                if (slice.Bytes.Length == 0)
                {
                    break;
                }

                all.AddRange(slice.Bytes);
            }

            return all.ToArray();
        }

        public X11PropertyData GetStoredProperty(ulong win, ulong prop) =>
            props.TryGetValue((win, prop), out var data)
                ? data
                : new X11PropertyData(false, 0, 0, [], [], 0);

        public void SetSelectionOwner(ulong selection, ulong owner, ulong time)
        {
            CurrentOwner = owner;
            events.Enqueue(new X11SelectionOwnerChanged(selection, owner, time));
        }

        public void ConvertSelection(ulong selection, ulong target, ulong property, ulong requestor, ulong time)
        {
            if (target == TargetsAtom)
            {
                props[(requestor, property)] = new X11PropertyData(true, AtomTypeAtom, 32, [], remoteTargets, 0);
                events.Enqueue(new X11SelectionNotify(requestor, selection, target, property, time));
                return;
            }

            if (remoteIncr)
            {
                var declared = stallAfterStart ? 4096UL : (ulong)remoteBytes.Length;
                props[(requestor, property)] = new X11PropertyData(true, IncrAtom, 32, [], [declared], 0);
                events.Enqueue(new X11SelectionNotify(requestor, selection, target, property, time));
                return;
            }

            props[(requestor, property)] = new X11PropertyData(true, target, 8, remoteBytes, [], 0);
            events.Enqueue(new X11SelectionNotify(requestor, selection, target, property, time));
        }

        public X11PropertyData GetWindowProperty(ulong window, ulong property, bool delete)
        {
            if (!props.TryGetValue((window, property), out var val))
            {
                return new X11PropertyData(false, 0, 0, [], [], 0);
            }

            if (delete)
            {
                props.Remove((window, property));
                if (remoteIncr && !stallAfterStart && (val.ActualType == IncrAtom || incrStreamStarted) && remoteIncrChunks.Count > 0)
                {
                    incrStreamStarted = true;
                    var nextChunk = remoteIncrChunks.Dequeue();
                    props[(window, property)] = new X11PropertyData(true, Utf8StringAtom, 8, nextChunk, [], 0);
                    events.Enqueue(new X11PropertyNotify(window, property, 99, Deleted: false));
                }
            }

            return val;
        }

        public void ChangePropertyBytes(ulong window, ulong property, ulong type, int format, ReadOnlySpan<byte> data) =>
            props[(window, property)] = new X11PropertyData(true, type, format, data.ToArray(), [], 0);

        public void ChangePropertyWords32(ulong window, ulong property, ulong type, ReadOnlySpan<ulong> words) =>
            props[(window, property)] = new X11PropertyData(true, type, 32, [], words.ToArray(), 0);

        public void DeleteProperty(ulong window, ulong property)
        {
            props.Remove((window, property));
            PropertyWasDeleted = true;
        }

        public void SelectPropertyNotify(ulong window) { }

        public void SendSelectionNotify(ulong requestor, ulong selection, ulong target, ulong property, ulong time) { }

        public void Flush() { }

        public X11Event? NextEvent(int timeoutMs) => events.Count > 0 ? events.Dequeue() : null;

        public void Dispose() { }
    }
}
