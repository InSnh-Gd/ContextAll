using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class ClipboardRulesTests
{
    static readonly byte[] SpkiA = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
    static readonly byte[] SpkiB = Enumerable.Range(0, 40).Select(i => (byte)(i + 3)).ToArray();
    static readonly byte[] SpkiC = Enumerable.Range(0, 40).Select(i => (byte)(255 - i)).ToArray();

    [Fact]
    public void Clock_order_is_milliseconds_then_counter_then_digest_and_the_counter_is_not_the_sequence()
    {
        var self = DeviceIds.Text(SpkiA);
        var other = DeviceIds.Text(SpkiB);
        var earlier = new HybridClock(10, 2, self);
        var laterCounter = new HybridClock(10, 3, other);
        var laterDigest = new HybridClock(10, 3, other);
        var same = new HybridClock(10, 3, self);

        Assert.True(HybridClock.Compare(laterCounter, earlier) > 0);
        Assert.True(HybridClock.Compare(new HybridClock(11, 0, self), laterCounter) > 0);
        Assert.True(HybridClock.Compare(laterDigest, same) != 0);

        var issued = HybridClocks.Issue(earlier, 10, self);
        var again = HybridClocks.Issue(issued, 10, self);
        Assert.True(HybridClock.Compare(issued, earlier) > 0);
        Assert.True(HybridClock.Compare(again, issued) > 0);

        var node = new ClipboardNode(self);
        node.ObserveRemoteClock(new HybridClock(5_000, 4, other));
        node.OnLocal(ClipboardKind.Plain, "x", 5_000);
        var clock = node.Clock;
        Assert.NotNull(clock);
        Assert.Equal(1, node.Vector[self]);
        Assert.NotEqual(node.Vector[self], (long)clock.Value.Counter);
    }

    [Fact]
    public void Sync_open_sends_the_current_text_only_when_it_is_newer_and_a_duplicate_is_ignored()
    {
        var self = DeviceIds.Text(SpkiA);
        var peer = DeviceIds.Text(SpkiB);
        var node = new ClipboardNode(self);
        node.Connect(peer, localEnabled: true, remoteEnabled: true);
        node.OnLocal(ClipboardKind.Plain, "now", 2_000);
        var open = node.OnSyncOpen(peer, [], new HybridClock(1, 0, peer), mutual: true);

        Assert.Contains(open, frame => frame.Type == FrameTypes.CrdtStateVector);
        var delta = open.Single(frame => frame.Type == FrameTypes.CrdtDeltaBatch);
        var payload = ControlJson.Decode(delta.Payload.Span, ProtocolJsonContext.Default.DeltaPayload).Value!;
        Assert.Equal("now", payload.Text);

        var muted = new ClipboardNode(self);
        muted.OnLocal(ClipboardKind.Plain, "kept", 2_000);
        var exchanged = muted.OnSyncOpen(peer, [], new HybridClock(9_000, 1, peer), mutual: false);
        Assert.Contains(exchanged, frame => frame.Type == FrameTypes.CrdtStateVector);
        Assert.DoesNotContain(exchanged, frame => frame.Type == FrameTypes.CrdtDeltaBatch);
        Assert.Equal("kept", muted.Text);

        var remote = new ClipboardNode(peer);
        remote.Connect(self, true, true);
        var sent = node.OnLocal(ClipboardKind.Plain, "once", 3_000).Single(item => item.PeerId == peer);
        var body = ControlJson.Decode(sent.Frame.Payload.Span, ProtocolJsonContext.Default.DeltaPayload).Value!;
        remote.ReceiveDelta(self, body, mutual: true);
        var applies = remote.ApplyCount;
        remote.ReceiveDelta(self, body, mutual: true);
        Assert.Equal(applies, remote.ApplyCount);
        Assert.Equal("once", remote.Text);
    }

    [Fact]
    public void A_delta_is_not_forwarded_and_is_not_sent_back_to_its_origin()
    {
        var a = new ClipboardNode(DeviceIds.Text(SpkiA));
        var b = new ClipboardNode(DeviceIds.Text(SpkiB));
        var cId = DeviceIds.Text(SpkiC);
        a.Connect(b.DeviceId, true, true);
        a.Connect(cId, true, true);
        b.Connect(a.DeviceId, true, true);
        b.Connect(cId, true, true);

        var sent = a.OnLocal(ClipboardKind.Plain, "hop", 4_000);
        Assert.Equal(2, sent.Count);
        Assert.DoesNotContain(sent, item => item.PeerId == a.DeviceId);
        var toB = sent.Single(item => item.PeerId == b.DeviceId);
        var delta = ControlJson.Decode(toB.Frame.Payload.Span, ProtocolJsonContext.Default.DeltaPayload).Value!;
        var forwarded = b.ReceiveDelta(a.DeviceId, delta, mutual: true);

        Assert.Empty(forwarded);
        Assert.Equal("hop", b.Text);
        Assert.Equal(0, b.Notifications);
        Assert.DoesNotContain(b.LastSent, item => item.PeerId == cId);
    }

    [Fact]
    public void Small_text_stays_in_one_delta_and_a_newer_clock_discards_an_incomplete_assembly()
    {
        var self = DeviceIds.Text(SpkiA);
        var peer = DeviceIds.Text(SpkiB);
        var node = new ClipboardNode(self);
        node.Connect(peer, true, true);
        var small = node.OnLocal(ClipboardKind.Plain, "fits", 1_000).Where(item => item.PeerId == peer).ToArray();
        Assert.Single(small);
        Assert.Equal(FrameTypes.CrdtDeltaBatch, small[0].Frame.Type);
        Assert.DoesNotContain(small, item => item.Frame.Type == FrameTypes.ClipboardChunk);

        node.OnLocal(ClipboardKind.Image, "nope", 1_100);
        Assert.Empty(node.LastSent);
        Assert.Equal("fits", node.Text);

        var receiver = new ClipboardNode(peer);
        receiver.Connect(self, true, true);
        var large = new string('a', 80_000);
        var parts = node.OnLocal(ClipboardKind.Plain, large, 1_200).Where(item => item.PeerId == peer).ToArray();
        Assert.Equal(FrameTypes.CrdtDeltaBatch, parts[0].Frame.Type);
        Assert.Contains(parts, item => item.Frame.Type == FrameTypes.ClipboardChunk);
        var header = ControlJson.Decode(parts[0].Frame.Payload.Span, ProtocolJsonContext.Default.DeltaPayload).Value!;
        Assert.Null(header.Text);
        Assert.NotNull(header.Sha256);
        receiver.ReceiveDelta(self, header, mutual: true);
        receiver.ReceiveChunk(header.StreamId!.Value, parts[1].Frame.Payload.Span, mutual: true);

        var newer = node.OnLocal(ClipboardKind.Plain, "newer", 5_000).Single(item => item.PeerId == peer);
        var newerDelta = ControlJson.Decode(newer.Frame.Payload.Span, ProtocolJsonContext.Default.DeltaPayload).Value!;
        receiver.ReceiveDelta(self, newerDelta, mutual: true);
        foreach (var chunk in parts.Skip(1))
        {
            receiver.ReceiveChunk(chunk.Frame.StreamId, chunk.Frame.Payload.Span, mutual: true);
        }

        Assert.Equal("newer", receiver.Text);

        var applied = new ClipboardNode(peer);
        applied.Connect(self, true, true);
        applied.ReceiveDelta(self, header, mutual: true);
        foreach (var chunk in parts.Skip(1))
        {
            applied.ReceiveChunk(chunk.Frame.StreamId, chunk.Frame.Payload.Span, mutual: true);
        }

        Assert.Equal(large, applied.Text);
        Assert.Equal(0, applied.Notifications);
    }

    [Fact]
    public void An_assembly_above_one_mebibyte_is_dropped_and_the_register_stays()
    {
        var self = DeviceIds.Text(SpkiA);
        var peer = DeviceIds.Text(SpkiB);
        var node = new ClipboardNode(self);
        node.Connect(peer, true, true);
        node.OnLocal(ClipboardKind.Plain, "kept", 1_000);
        var seq = 9L;
        foreach (var count in new long[] { 0, -1, ClipboardRestart.MaxBytes + 1 })
        {
            node.ReceiveDelta(peer, new DeltaPayload(peer, seq, 2_000, 1, null, 4, count, new string('a', 64)), mutual: true);
            node.ReceiveChunk(4, [0, 0, 0, 0, (byte)'x'], mutual: true);
            Assert.Equal("kept", node.Text);
            seq++;
        }

        node.ReceiveDelta(peer, new DeltaPayload(peer, seq, 3_000, 1, "next", null, null, null), mutual: true);
        Assert.Equal("next", node.Text);
    }

    [Fact]
    public void A_chunk_header_is_not_buffered_until_sync_is_mutual()
    {
        var self = DeviceIds.Text(SpkiA);
        var peer = DeviceIds.Text(SpkiB);
        var node = new ClipboardNode(self);
        node.Connect(peer, true, false);
        node.OnLocal(ClipboardKind.Plain, "kept", 1_000);
        var count = (long)ClipboardRestart.MaxBytes;
        var before = GC.GetAllocatedBytesForCurrentThread();
        node.ReceiveDelta(peer, new DeltaPayload(peer, 4, 2_000, 1, null, 9, count, new string('a', 64)), mutual: false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        node.ReceiveChunk(9, [0, 0, 0, 0, (byte)'x'], mutual: true);

        Assert.True(allocated < count);
        Assert.Equal("kept", node.Text);
    }

    [Fact]
    public void Restart_keeps_the_sequence_for_the_same_text_and_still_listens_without_the_file()
    {
        var stored = new ClipboardDocument("same", new HybridClock(1, 0, DeviceIds.Text(SpkiA)), new Dictionary<string, long> { ["ca1"] = 3 }, new HybridClock(1, 0, DeviceIds.Text(SpkiA)), "old", 3);
        var same = ClipboardRestart.Plan(stored, missingOrUnreadable: false, "same", isPlain: true);
        Assert.Equal(3, same.Document.Sequence);
        Assert.Equal("same", same.Document.AppliedText);
        Assert.Equal(0, same.WindowCount);

        var edited = ClipboardRestart.Plan(stored, false, "edited", true);
        Assert.True(edited.NewLocalChange);
        Assert.Equal(4, edited.Document.Sequence);

        var pending = ClipboardRestart.Plan(stored, false, "old", true);
        Assert.False(pending.NewLocalChange);
        Assert.True(pending.WriteClipboard);
        Assert.Equal("same", pending.ClipboardText);

        var missing = ClipboardRestart.Plan(null, true, "hello", true);
        Assert.True(missing.Listening);
        Assert.Equal(0, missing.WindowCount);
    }
}
