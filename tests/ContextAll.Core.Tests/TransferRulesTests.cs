using System.Text.Json;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class TransferRulesTests
{
    const string Peer = "ca1peerpeerpeerpeerpeerpeer";

    [Fact]
    public void An_offer_carries_the_root_hash_and_a_segment_is_stored_before_its_bit()
    {
        var book = new TransferBook();
        book.Ready();
        var content = new byte[] { 1, 2, 3, 4 };
        Assert.Null(book.Offer(Peer, "notes.txt", content, sessionUp: true));
        var offer = book.Frames.Single(frame => frame.Type == FrameTypes.FileOffer);
        var json = JsonDocument.Parse(offer.Payload);
        Assert.Equal(64, json.RootElement.GetProperty("rootSha256").GetString()!.Length);
        Assert.False(json.RootElement.TryGetProperty("segmentHashes", out _));
        Assert.Equal(TransferBook.SegmentSize, json.RootElement.GetProperty("segmentSize").GetInt32());

        var inbound = OfferTo(book, content);
        book.ReceiveSegment(Peer, 0, System.Security.Cryptography.SHA256.HashData(content), content, content);
        Assert.Equal(["write", "flush", "bit"], book.Works.Select(work => work.Name).ToArray());
        Assert.Equal(0, book.Works[0].Offset);

        var resumed = new TransferBook();
        var slot = resumed.SeedOutbound(Peer, 6, 0, 2, 4);
        resumed.Release(Peer);
        Assert.Equal([1, 3, 5], Indexes(resumed));
        Assert.Equal(6, slot.SegmentCount);
    }

    [Fact]
    public void The_ninth_segment_waits_and_a_bitmap_is_split_before_data()
    {
        var book = new TransferBook();
        var slot = book.SeedOutbound(Peer, 9);
        book.Release(Peer);
        Assert.Equal(8, book.Frames.Count(frame => frame.Type == FrameTypes.FileSegment));
        book.Ack(slot.TransferId, 0);
        Assert.Equal(8, Indexes(book).Single());

        slot.BitmapComplete = false;
        slot.Flying.Clear();
        book.Release(Peer);
        Assert.DoesNotContain(book.Frames, frame => frame.Type == FrameTypes.FileSegment);

        var bits = new byte[70_000];
        var parts = TransferBook.SplitBitmap(slot.TransferId, bits);
        Assert.True(parts.Count > 1);
        Assert.NotEqual(0, parts[0].Flags & FrameTypes.BitmapContinued);
        Assert.Equal(0, parts[^1].Flags & FrameTypes.BitmapContinued);
        Assert.Contains(parts, frame => frame.Payload.Length > 0);
        book.ReceiveBitmap(slot.TransferId, 0, bits, last: false);
        Assert.Empty(book.Frames);
        book.ReceiveBitmap(slot.TransferId, bits.Length * 8L, [], last: true);

        var mismatched = new TransferBook();
        mismatched.Ready();
        mismatched.Offer(Peer, "out.bin", [9], true);
        OfferTo(mismatched, [1, 2]);
        mismatched.ReceiveSegment(Peer, 0, new byte[32], new byte[] { 1, 2 }, null);
        Assert.NotNull(mismatched.Outbound(Peer));
        Assert.Null(mismatched.Inbound(Peer));
        Assert.Contains(mismatched.Outcomes, outcome => outcome.Outcome == "checksum-failed");
    }

    [Fact]
    public void One_peer_can_send_and_receive_and_cancel_leaves_the_other_direction()
    {
        var book = new TransferBook();
        book.Ready();
        Assert.Null(book.Offer(Peer, "out.bin", [1], true));
        OfferTo(book, [2]);
        Assert.NotNull(book.Outbound(Peer));
        Assert.NotNull(book.Inbound(Peer));
        Assert.Equal("busy", book.Offer(Peer, "other.bin", [3], true));
        Assert.Equal("out.bin", book.Outbound(Peer)!.FileName);

        var clipboard = new ClipboardNode(DeviceIds.Text(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()));
        clipboard.OnLocal(ClipboardKind.Plain, "still", 10);
        book.Cancel(book.Inbound(Peer)!.TransferId, sessionUp: true);
        Assert.Null(book.Inbound(Peer));
        Assert.Equal("out.bin", book.Outbound(Peer)!.FileName);
        Assert.Equal("still", clipboard.Text);
    }

    [Fact]
    public void Reconnect_announces_outbound_transfers_and_rejects_an_early_offer()
    {
        var early = new TransferBook();
        Assert.Equal("busy", early.ReceiveOffer(Sample([1], 1), Peer, false, 0));
        Assert.Null(early.Inbound(Peer));

        var book = new TransferBook();
        var slot = book.SeedOutbound(Peer, 4, 0);
        book.ClearFrames();
        book.BecomeTrusted(Peer, [slot]);
        Assert.Contains(book.Frames, frame => frame.Type == FrameTypes.FileResume);
        Assert.Contains(book.Frames, frame => System.Text.Encoding.UTF8.GetString(frame.Payload.Span).Contains("\"done\":true", StringComparison.Ordinal));
        book.ResumeUnknown(slot.TransferId);
        Assert.Contains(book.Frames, frame => frame.Type == FrameTypes.FileReject);
        Assert.Contains(book.Works, work => work.Name == "delete");

        var held = new TransferBook();
        Assert.Equal("held", held.Offer(Peer, "later.bin", [4], true));
        Assert.Equal("busy", held.Offer(Peer, "nope.bin", [5], true));
        held.FinishAnnouncement();
        Assert.Equal("later.bin", held.Outbound(Peer)!.FileName);
    }

    [Fact]
    public void Accept_sends_an_empty_bitmap_and_a_failed_move_keeps_the_slot()
    {
        var book = new TransferBook();
        book.Ready();
        var offer = OfferTo(book, []);
        Assert.Equal(FrameTypes.FileAccept, book.Frames[0].Type);
        Assert.Equal(FrameTypes.FileBitmap, book.Frames[1].Type);
        Assert.DoesNotContain(book.Frames, frame => frame.Type == FrameTypes.FileSegment);
        _ = offer;

        var prompted = new TransferBook();
        prompted.Ready();
        var waiting = Sample([7], 1);
        Assert.Null(prompted.ReceiveOffer(waiting, Peer, askBeforeSave: true, nowMs: 0));
        Assert.Empty(prompted.Works);
        Assert.Equal("timeout", prompted.Timeout(Peer, 60_000));
        Assert.Null(prompted.Inbound(Peer));

        var named = DownloadNames.Sanitize(@"dir\a.txt");
        Assert.Equal("a.txt", named);
        Assert.Equal("a (2).txt", DownloadNames.AvoidCollision(named, existing => existing == "a.txt"));
        Assert.Equal("download", DownloadNames.Sanitize("CON"));

        var blocked = new TransferBook();
        blocked.Ready();
        OfferTo(blocked, [8]);
        var inbound = blocked.Inbound(Peer)!;
        Assert.False(blocked.TryMove(inbound, renameOk: false, copyOk: false));
        Assert.True(inbound.MoveBlocked);
        Assert.NotNull(blocked.Inbound(Peer));
    }

    [Fact]
    public void An_offer_above_one_gigabyte_does_not_take_a_slot()
    {
        var book = new TransferBook();
        book.Ready();
        var tooBig = TransferBook.MaxFileBytes + 1;
        var rejected = Sample([], 0) with
        {
            TotalSize = tooBig,
            SegmentCount = (tooBig + TransferBook.SegmentSize - 1) / TransferBook.SegmentSize,
        };
        Assert.Equal("invalid", book.ReceiveOffer(rejected, Peer, askBeforeSave: true, nowMs: 0));
        Assert.Null(book.Inbound(Peer));
        Assert.Contains(book.Frames, frame => frame.Type == FrameTypes.FileReject);

        var allowed = new TransferBook();
        allowed.Ready();
        var atCap = Sample([], 0) with
        {
            TotalSize = TransferBook.MaxFileBytes,
            SegmentCount = (TransferBook.MaxFileBytes + TransferBook.SegmentSize - 1) / TransferBook.SegmentSize,
        };
        Assert.Null(allowed.ReceiveOffer(atCap, Peer, askBeforeSave: true, nowMs: 0));
        Assert.NotNull(allowed.Inbound(Peer));
        Assert.Empty(allowed.Works);
    }

    [Fact]
    public void Unpair_cancels_both_directions_and_frees_the_slots()
    {
        var live = new TransferBook();
        live.Ready();
        Assert.Null(live.Offer(Peer, "out.bin", [1], true));
        OfferTo(live, [2]);
        live.ClearFrames();
        live.CancelOutstanding(sessionUp: true);
        Assert.Equal(2, live.Frames.Count(frame => frame.Type == FrameTypes.FileCancel));
        Assert.Equal(2, live.Works.Count(work => work.Name == "delete"));
        Assert.Null(live.Inbound(Peer));
        Assert.Null(live.Outbound(Peer));

        var down = new TransferBook();
        down.Ready();
        Assert.Null(down.Offer(Peer, "out.bin", [1], true));
        OfferTo(down, [2]);
        down.ClearFrames();
        down.CancelOutstanding(sessionUp: false);
        Assert.Empty(down.Frames);
        Assert.Equal(2, down.Works.Count(work => work.Name == "delete"));
        Assert.Null(down.Inbound(Peer));
        Assert.Null(down.Outbound(Peer));
    }

    static FileOfferPayload OfferTo(TransferBook book, byte[] content)
    {
        var offer = Sample(content, content.Length == 0 ? 0 : 1);
        Assert.Null(book.ReceiveOffer(offer, Peer, askBeforeSave: false, nowMs: 0));
        return offer;
    }

    static FileOfferPayload Sample(byte[] content, long segmentCount) =>
        new(
            new string('c', 32),
            2,
            content.Length,
            TransferBook.SegmentSize,
            segmentCount,
            new string('d', 64),
            "a.txt");

    static long[] Indexes(TransferBook book) =>
        book.Frames
            .Where(frame => frame.Type == FrameTypes.FileSegment)
            .Select(frame => (long)((frame.Payload.Span[0] << 24) | (frame.Payload.Span[1] << 16) | (frame.Payload.Span[2] << 8) | frame.Payload.Span[3]))
            .ToArray();
}
