using System.Buffers;
using System.Buffers.Binary;
using System.Net.Quic;
using System.Runtime.InteropServices;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Host;

public sealed class PeerLink
{
    readonly LanHost host;
    readonly QuicConnection connection;
    readonly QuicStream stream;
    readonly PairingSession pairing;
    readonly TransferBook book = new();
    readonly FrameOutbox outbox = new();
    readonly Queue<Frame> immediate = new();
    readonly object writeGate = new();
    readonly SemaphoreSlim poke = new(0);
    readonly List<string> announced = [];
    readonly CancellationTokenSource cancel = new();
    int pairingSent;
    bool helloSent;
    bool entered;
    bool closed;
    long readyTick = -1;
    Task? writer;

    public PeerLink(LanHost host, QuicConnection connection, QuicStream stream, byte[] remoteSpki, bool initiated)
    {
        this.host = host;
        this.connection = connection;
        this.stream = stream;
        RemoteSpki = remoteSpki.ToArray();
        RemoteId = DeviceIds.Text(RemoteSpki);
        Initiated = initiated;
        var initiator = initiated ? host.LocalSpki : RemoteSpki;
        pairing = PairingSession.Open(host.LocalSpki, RemoteSpki, initiator, host.DisplayName);
    }

    public string RemoteId { get; }

    public byte[] RemoteSpki { get; }

    public bool Initiated { get; }

    public string RemoteIntent { get; private set; } = "pairing";

    public bool Closed => closed;

    public bool Entered => entered;

    public bool BothHellos => helloSent && pairing.RemoteHelloSeen;

    public bool Prompting => pairing.IsSurvivor && !pairing.Trusted && !pairing.Closed && !entered;

    public bool LocalAccepted => pairing.LocalAccepted;

    public string? Code => pairing.ShortCode;

    public string RemoteName => pairing.RemoteName;

    public long RemainingMs
    {
        get
        {
            if (readyTick < 0)
            {
                return 60_000;
            }

            return Math.Max(0, 60_000 - (Environment.TickCount64 - readyTick));
        }
    }

    public async Task RunAsync()
    {
        writer = Task.Run(WriteLoop);
        lock (host.Gate)
        {
            pairing.OnWritable();
            helloSent = true;
            var frames = NewPairingFrames();
            if (host.IsTrusted(RemoteId))
            {
                frames.RemoveAll(frame => frame.Type == FrameTypes.Hello);
                frames.Insert(0, Hello("trusted"));
            }

            PostImmediate(frames);
        }

        using var hello = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var timeout = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, hello.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (host.Gate)
                {
                    if (!pairing.RemoteHelloSeen)
                    {
                        Close();
                    }
                }
            }
        });
        _ = Task.Run(PairingClock);
        try
        {
            await ReadLoop().ConfigureAwait(false);
        }
        finally
        {
            hello.Cancel();
            cancel.Cancel();
            lock (host.Gate)
            {
                Close();
            }

            poke.Release();
            try
            {
                if (writer is not null)
                {
                    await writer.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The session closed.
            }

            host.LinkClosed(this);
        }
    }

    public void Adopt()
    {
        pairing.AdoptIfOnly();
        NoteSurvivor();
    }

    public void ResolveDuplicate(PeerLink other)
    {
        PairingSession.Deduplicate(pairing, other.pairing);
        PostImmediate(NewPairingFrames());
        other.PostImmediate(other.NewPairingFrames());
        FinishDuplicate(this);
        FinishDuplicate(other);
    }

    public void SendUnpair()
    {
        lock (host.Gate)
        {
            book.CancelOutstanding(sessionUp: true);
            var (works, frames) = Take();
            Apply(works);
            var outgoing = new Frame[frames.Length + 1];
            for (var i = 0; i < frames.Length; i++)
            {
                outgoing[i] = frames[i];
            }

            outgoing[^1] = FrameMessages.Json(FrameTypes.Unpair, 0, 0, FrameTypes.SessionStream, new DonePayload(true), ProtocolJsonContext.Default.DonePayload);
            Post(outgoing);
            Close();
        }
    }

    public void AbandonTransfers()
    {
        lock (host.Gate)
        {
            book.CancelOutstanding(sessionUp: false);
            Apply(Take().Works);
        }
    }

    public void PostFrames(IReadOnlyList<Frame> frames) => Post(frames);

    static void FinishDuplicate(PeerLink link)
    {
        if (link.pairing.Closed)
        {
            link.host.EndPairing(link, "duplicate");
            link.Close();
            return;
        }

        link.NoteSurvivor();
    }

    public string? AcceptFromUser()
    {
        if (closed || !Prompting || pairing.LocalAccepted)
        {
            return "already-decided";
        }

        pairing.AcceptLocal();
        Post(FrameMessages.Json(FrameTypes.PairAccept, 0, 0, FrameTypes.SessionStream, new DonePayload(true), ProtocolJsonContext.Default.DonePayload));
        Post(NewPairingFrames());
        if (pairing.Trusted)
        {
            EnterTrusted();
        }

        return null;
    }

    public string? RejectFromUser()
    {
        if (closed || !Prompting || pairing.LocalAccepted)
        {
            return "already-decided";
        }

        pairing.Reject();
        Post(NewPairingFrames());
        host.EndPairing(this, "rejected");
        Close();
        return null;
    }

    public string? OfferFile(string path)
    {
        if (!entered)
        {
            return "busy";
        }

        Align();
        var bytes = File.ReadAllBytes(path);
        var name = Path.GetFileName(path);
        var error = book.Offer(RemoteId, name, bytes, sessionUp: true);
        if (error is not null)
        {
            return error;
        }

        var (works, frames) = Take();
        _ = works;
        var offer = frames.Select(DecodeOffer).FirstOrDefault(item => item is not null);
        if (offer is not null)
        {
            host.RememberBytes(offer.TransferId, bytes);
            var slot = book.Outbound(RemoteId);
            if (slot is not null)
            {
                host.Store.Create(slot, bytes);
            }
        }

        PostExpanded(frames);
        host.Clipboard.NextBulk = book.NextBulk;
        return null;
    }

    public string? AcceptFile(string transferId)
    {
        var error = book.Accept(transferId, Environment.TickCount64);
        if (error is not null)
        {
            return error;
        }

        var (works, frames) = Take();
        Apply(works);
        PostExpanded(frames);
        host.Publish([Progress(book.Inbound(RemoteId), ended: null)]);
        return null;
    }

    public string? RejectFile(string transferId)
    {
        var error = book.RejectPrompt(transferId);
        if (error is not null)
        {
            return error;
        }

        var (works, frames) = Take();
        Apply(works);
        Post(frames);
        return null;
    }

    public string? CancelFile(string transferId)
    {
        if (book.Incoming.All(slot => slot.TransferId != transferId) && book.Outgoing.All(slot => slot.TransferId != transferId))
        {
            var stored = host.Store.Load(transferId);
            if (stored is null || stored.PeerId != RemoteId)
            {
                return "unknown-transfer";
            }

            host.Store.Delete(transferId);
            return null;
        }

        book.Cancel(transferId, sessionUp: !closed);
        var (works, frames) = Take();
        Apply(works);
        Post(frames);
        return null;
    }

    public void PushClipboard()
    {
        if (!entered)
        {
            return;
        }

        Align();
        var mutual = host.Mutual(RemoteId);
        var frames = host.Clipboard.OnSyncOpen(RemoteId, [], host.RemoteClock(RemoteId), mutual).ToArray();
        book.NextBulk = FrameMessages.BulkAfter(frames, book.NextBulk);
        host.Clipboard.NextBulk = book.NextBulk;
        Post(frames.Select(frame => frame.Type == FrameTypes.CrdtStateVector ? host.WithSyncFlag(frame, host.LocalClipboard(RemoteId)) : frame).ToArray());
    }

    public void Collect(List<FilePromptSnapshotPayload> prompts, List<TransferSnapshotPayload> transfers)
    {
        foreach (var slot in book.Incoming)
        {
            if (slot.WaitingForAccept)
            {
                prompts.Add(new FilePromptSnapshotPayload(slot.TransferId, RemoteId, slot.FileName, slot.TotalSize, Math.Max(0, 60_000 - (Environment.TickCount64 - slot.PromptStartedAt))));
            }
            else if (slot.Accepted && slot.Completed < slot.SegmentCount)
            {
                transfers.Add(TransferView(slot));
            }
        }

        foreach (var slot in book.Outgoing)
        {
            if (slot.Completed < slot.SegmentCount)
            {
                transfers.Add(TransferView(slot));
            }
        }
    }

    async Task PairingClock()
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                await Task.Delay(1000, cancel.Token).ConfigureAwait(false);
                Frame[]? promptEvents = null;
                lock (host.Gate)
                {
                    if (closed)
                    {
                        continue;
                    }

                    if (!entered && readyTick >= 0)
                    {
                        pairing.OnClock(Environment.TickCount64 - readyTick);
                        if (pairing.Closed && !pairing.Trusted)
                        {
                            host.EndPairing(this, "timeout");
                            Close();
                            continue;
                        }
                    }

                    var waiting = book.Inbound(RemoteId);
                    if (book.Timeout(RemoteId, Environment.TickCount64) is string outcome)
                    {
                        var (works, frames) = Take();
                        Apply(works);
                        Post(frames);
                        promptEvents = [Ended(waiting?.TransferId ?? "", outcome)];
                    }
                }

                if (promptEvents is not null)
                {
                    host.Publish(promptEvents);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session closed.
        }
    }

    async Task ReadLoop()
    {
        var buffer = new byte[FrameLimits.HeaderBytes + FrameLimits.MaxPayloadBytes];
        var filled = 0;
        try
        {
            while (!closed)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(filled), cancel.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                filled += read;
                while (true)
                {
                    var parsed = FrameCodec.Read(buffer.AsSpan(0, filled));
                    if (parsed.NeedMore)
                    {
                        break;
                    }

                    if (parsed.IsFailure || parsed.Frame is null || parsed.Frame.Type >= 64)
                    {
                        lock (host.Gate)
                        {
                            Close();
                        }

                        return;
                    }

                    Frame[] outgoing;
                    Frame[] events;
                    lock (host.Gate)
                    {
                        if (closed)
                        {
                            return;
                        }

                        (outgoing, events) = Handle(parsed.Frame);
                    }

                    Post(outgoing);
                    if (events.Length > 0)
                    {
                        host.Publish(events);
                    }

                    host.FlushClipboard();

                    var rest = filled - parsed.Consumed;
                    Buffer.BlockCopy(buffer, parsed.Consumed, buffer, 0, rest);
                    filled = rest;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or QuicException)
        {
            // The session closed.
        }
    }

    internal void MarkWritable()
    {
        pairing.OnWritable();
        helloSent = true;
    }

    internal (Frame[] Outgoing, Frame[] Events) Dispatch(Frame frame) => Handle(frame);

    (Frame[] Outgoing, Frame[] Events) Handle(Frame frame)
    {
        if (!entered && frame.Type is not (FrameTypes.Hello or FrameTypes.PairAccept or FrameTypes.PairReject or FrameTypes.SessionDuplicateClose))
        {
            return (Array.Empty<Frame>(), Array.Empty<Frame>());
        }

        var events = new List<Frame>();
        switch (frame.Type)
        {
            case FrameTypes.Hello:
                var hello = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.HelloPayload).Value;
                if (hello is null)
                {
                    Close();
                    break;
                }

                RemoteIntent = hello.Intent;
                pairing.ReceiveHello(hello.DisplayName);
                host.Settle(this);
                NoteSurvivor();
                break;
            case FrameTypes.PairAccept:
                pairing.AcceptRemote();
                if (pairing.Trusted)
                {
                    EnterTrusted();
                    events.Add(host.DeviceEvent(RemoteId, "connected"));
                }

                break;
            case FrameTypes.PairReject:
            case FrameTypes.SessionDuplicateClose:
                pairing.Reject();
                host.EndPairing(this, "rejected");
                Close();
                break;
            case FrameTypes.DisplayName:
                var name = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.DisplayNamePayload).Value;
                if (name is not null && entered)
                {
                    host.RenamePeer(RemoteId, name.DisplayName);
                    events.Add(host.DeviceEvent(RemoteId, "connected"));
                }

                break;
            case FrameTypes.PathAnnounce:
                var paths = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PathAnnouncePayload).Value;
                if (paths is not null)
                {
                    host.NoteAnnouncement(RemoteId, paths.Addresses);
                }

                break;
            case FrameTypes.Unpair:
                host.RemoveTrust(RemoteId, notify: false);
                Close();
                break;
            case FrameTypes.FileOffer:
                events.AddRange(OnOffer(frame));
                break;
            case FrameTypes.FileAccept:
                break;
            case FrameTypes.FileReject:
                OnReject(frame);
                break;
            case FrameTypes.FileResume:
                events.AddRange(OnResume(frame));
                break;
            case FrameTypes.FileBitmap:
                OnBitmap(frame);
                break;
            case FrameTypes.FileSegment:
                events.AddRange(OnSegment(frame));
                break;
            case FrameTypes.FileAck:
                OnAck(frame);
                break;
            case FrameTypes.FileCancel:
                var cancelPayload = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileCancelPayload).Value;
                if (cancelPayload is not null)
                {
                    CancelFile(cancelPayload.TransferId);
                }

                break;
            case FrameTypes.CrdtStateVector:
                OnVector(frame);
                break;
            case FrameTypes.CrdtDeltaBatch:
                events.AddRange(OnDelta(frame));
                break;
            case FrameTypes.ClipboardChunk:
                OnChunk(frame);
                break;
        }

        return (Array.Empty<Frame>(), events.ToArray());
    }

    void EnterTrusted()
    {
        if (entered || closed)
        {
            return;
        }

        entered = true;
        host.OnEntered(this);
        host.Clipboard.Connect(RemoteId, host.LocalClipboard(RemoteId), host.RemoteClipboard(RemoteId));
        host.SaveTrust(new TrustEntry(RemoteId, pairing.RemoteName, Convert.ToBase64String(RemoteSpki), false, true));
        var slots = host.Store.Incomplete(RemoteId, "send");
        foreach (var slot in slots)
        {
            if (host.Store.Exists(slot.TransferId))
            {
                host.RememberBytes(slot.TransferId, host.Store.Read(slot.TransferId));
            }
        }

        book.BecomeTrusted(RemoteId, slots);
        var (_, resumeFrames) = Take();
        Align();
        var mutual = host.Mutual(RemoteId);
        var sync = host.Clipboard.OnSyncOpen(RemoteId, [], null, mutual);
        book.NextBulk = FrameMessages.BulkAfter(sync, book.NextBulk);
        host.Clipboard.NextBulk = book.NextBulk;
        var ordered = new List<Frame>();
        ordered.AddRange(sync.Where(frame => frame.Type == FrameTypes.CrdtStateVector).Select(frame => host.WithSyncFlag(frame, host.LocalClipboard(RemoteId))));
        ordered.AddRange(resumeFrames);
        ordered.AddRange(sync.Where(frame => frame.Type != FrameTypes.CrdtStateVector));
        if (pairing.TryAnnounce(host.LocalAddresses()))
        {
            ordered.AddRange(NewPairingFrames());
        }

        PostExpanded(ordered, immediate: true);
        host.Publish([host.DeviceEvent(RemoteId, "connected")]);
    }

    List<Frame> OnOffer(Frame frame)
    {
        var offer = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileOfferPayload).Value;
        if (offer is null)
        {
            return [];
        }

        var events = new List<Frame>();
        book.ReceiveOffer(offer, RemoteId, host.Ask(RemoteId), Environment.TickCount64);
        var (works, frames) = Take();
        Apply(works);
        PostExpanded(frames);
        var slot = book.Inbound(RemoteId);
        if (slot is { WaitingForAccept: true })
        {
            events.Add(FrameMessages.Json(
                FrameTypes.FilePrompt,
                0,
                0,
                FrameTypes.SessionStream,
                new FilePromptSnapshotPayload(slot.TransferId, RemoteId, slot.FileName, slot.TotalSize, 60_000),
                ProtocolJsonContext.Default.FilePromptSnapshotPayload));
        }

        return events;
    }

    void OnReject(Frame frame)
    {
        var reject = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileRejectPayload).Value;
        if (reject is null)
        {
            return;
        }

        book.Cancel(reject.TransferId, sessionUp: false);
        var (works, _) = Take();
        Apply(works);
    }

    List<Frame> OnResume(Frame frame)
    {
        var done = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.DonePayload).Value;
        if (done is { Done: true })
        {
            book.PeerDone(RemoteId, announced);
            var (works, _) = Take();
            Apply(works);
            return [];
        }

        var resume = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileResumePayload).Value;
        if (resume is null || string.IsNullOrEmpty(resume.TransferId))
        {
            return [];
        }

        announced.Add(resume.TransferId);
        var slot = host.Store.Load(resume.TransferId);
        if (slot is null || slot.PeerId != RemoteId || slot.Direction != "receive")
        {
            book.ResumeUnknown(resume.TransferId);
            var (works, frames) = Take();
            Apply(works);
            Post(frames);
            return [];
        }

        slot.StreamId = resume.StreamId;
        book.RestoreInbound(slot);
        Post(TransferBook.SplitBitmap(slot.TransferId, slot.Bits).ToArray());
        return [];
    }

    void OnBitmap(Frame frame)
    {
        var bitmap = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileBitmapPayload).Value;
        if (bitmap is null)
        {
            return;
        }

        var bits = string.IsNullOrEmpty(bitmap.Bits) ? [] : Convert.FromBase64String(bitmap.Bits);
        var last = (frame.Flags & FrameTypes.BitmapContinued) == 0;
        book.ReceiveBitmap(bitmap.TransferId, bitmap.BitOffset, bits, last);
        var (_, frames) = Take();
        PostExpanded(frames);
    }

    List<Frame> OnSegment(Frame frame)
    {
        var slot = book.Inbound(RemoteId);
        if (frame.Payload.Length < 36 || slot is null || frame.StreamId != slot.StreamId)
        {
            return [];
        }

        var index = BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span);
        var hash = frame.Payload.Span.Slice(4, 32);
        var data = frame.Payload.Slice(36);
        var before = book.Outcomes.Count;
        book.ReceiveSegment(RemoteId, index, hash, data, null);
        var (works, frames) = Take();
        Apply(works);
        Post(frames);
        return Finish(before);
    }

    void OnAck(Frame frame)
    {
        var ack = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileAckPayload).Value;
        if (ack is null)
        {
            return;
        }

        book.Ack(ack.TransferId, ack.SegmentIndex);
        var (works, frames) = Take();
        Apply(works);
        TransferSlot? slot = null;
        foreach (var item in book.Outgoing)
        {
            if (item.TransferId == ack.TransferId)
            {
                slot = item;
                break;
            }
        }

        if (slot is not null)
        {
            host.Store.Save(slot);
            if (slot.Completed >= slot.SegmentCount)
            {
                host.Store.Delete(slot.TransferId);
                host.ForgetBytes(slot.TransferId);
                book.DropOutbound(slot.TransferId);
            }
        }

        PostExpanded(frames);
    }

    void OnVector(Frame frame)
    {
        var vector = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.VectorPayload).Value;
        if (vector is null)
        {
            return;
        }

        host.NoteRemoteClipboard(RemoteId, vector.ClipboardSync == true);
        if (vector.Clock is ClockPayload clock)
        {
            var remoteClock = new HybridClock(clock.Milliseconds, clock.Counter, clock.Origin);
            host.RememberRemoteClock(RemoteId, remoteClock);
            host.Clipboard.ObserveRemoteClock(remoteClock);
        }

        host.Clipboard.Connect(RemoteId, host.LocalClipboard(RemoteId), host.RemoteClipboard(RemoteId));
        if (!host.Mutual(RemoteId) || host.Clipboard.Clock is not HybridClock ours)
        {
            return;
        }

        if (vector.Clock is ClockPayload remote && HybridClock.Compare(ours, new HybridClock(remote.Milliseconds, remote.Counter, remote.Origin)) <= 0)
        {
            return;
        }

        Align();
        var follow = host.Clipboard.OnSyncOpen(RemoteId, vector.Versions, vector.Clock is ClockPayload seen ? new HybridClock(seen.Milliseconds, seen.Counter, seen.Origin) : null, true);
        book.NextBulk = FrameMessages.BulkAfter(follow, book.NextBulk);
        host.Clipboard.NextBulk = book.NextBulk;
        Post(follow.Where(item => item.Type != FrameTypes.CrdtStateVector).ToArray());
    }

    List<Frame> OnDelta(Frame frame)
    {
        var delta = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.DeltaPayload).Value;
        if (delta is null)
        {
            return [];
        }

        var before = host.Clipboard.ApplyCount;
        var notes = host.Clipboard.Notifications;
        host.Clipboard.ReceiveDelta(RemoteId, delta, host.Mutual(RemoteId));
        if (host.Clipboard.ApplyCount == before)
        {
            return [];
        }

        var previous = host.Applied;
        host.SaveClipboard(previous);
        host.WriteClipboard(host.Clipboard.Text);
        host.Applied = host.Clipboard.Text;
        host.SaveClipboard(host.Applied);
        if (host.Clipboard.Notifications != notes)
        {
            File.WriteAllText(host.NotifyLog, "raised");
        }

        return [];
    }

    void OnChunk(Frame frame)
    {
        var before = host.Clipboard.ApplyCount;
        var notes = host.Clipboard.Notifications;
        host.Clipboard.ReceiveChunk(frame.StreamId, frame.Payload.Span, host.Mutual(RemoteId));
        if (host.Clipboard.ApplyCount == before)
        {
            return;
        }

        var previous = host.Applied;
        host.SaveClipboard(previous);
        host.WriteClipboard(host.Clipboard.Text);
        host.Applied = host.Clipboard.Text;
        host.SaveClipboard(host.Applied);
        if (host.Clipboard.Notifications != notes)
        {
            File.WriteAllText(host.NotifyLog, "raised");
        }
    }

    List<Frame> Finish(int before)
    {
        var events = new List<Frame>();
        for (var i = before; i < book.Outcomes.Count; i++)
        {
            var outcome = book.Outcomes[i].Outcome;
            var slot = book.Inbound(RemoteId);
            if (outcome == "ready" && slot is not null)
            {
                var transferId = slot.TransferId;
                if (host.Store.TryPlace(slot, host.Downloads))
                {
                    book.TryMove(slot, true, false);
                    Apply(Take().Works);
                    events.Add(Ended(transferId, "completed"));
                }
                else
                {
                    host.Store.Delete(transferId);
                    book.Cancel(transferId, false);
                    Apply(Take().Works);
                    events.Add(Ended(transferId, "checksum-failed"));
                }
            }
            else if (outcome is "checksum-failed" or "gone" or "timeout")
            {
                events.Add(Ended(slot?.TransferId ?? "", outcome));
            }
        }

        return events;
    }

    void Apply(IReadOnlyList<TransferWork> works)
    {
        foreach (var work in works)
        {
            switch (work.Name)
            {
                case "create":
                    var created = Find(work.TransferId);
                    if (created is not null)
                    {
                        host.Store.Create(created, created.Direction == "send" && host.TryBytes(created.TransferId, out var bytes) ? bytes : null);
                    }

                    break;
                case "write":
                    host.Store.Write(work.TransferId, work.Offset, work.Data.Span);
                    break;
                case "flush":
                    host.Store.Flush(work.TransferId);
                    break;
                case "bit":
                    var current = Find(work.TransferId);
                    if (current is not null)
                    {
                        host.Store.Save(current);
                    }

                    break;
                case "delete":
                    host.Store.Delete(work.TransferId);
                    host.ForgetBytes(work.TransferId);
                    break;
            }
        }
    }

    TransferSlot? Find(string transferId)
    {
        foreach (var slot in book.Incoming)
        {
            if (slot.TransferId == transferId)
            {
                return slot;
            }
        }

        foreach (var slot in book.Outgoing)
        {
            if (slot.TransferId == transferId)
            {
                return slot;
            }
        }

        return null;
    }

    void PostExpanded(IReadOnlyList<Frame> frames, bool immediate = false)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var expanded = new Frame[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            expanded[i] = Expand(frames[i]);
        }

        if (immediate)
        {
            PostImmediate(expanded);
        }
        else
        {
            Post(expanded);
        }
    }

    (TransferWork[] Works, Frame[] Frames) Take()
    {
        var works = book.Works.ToArray();
        var frames = book.Frames.ToArray();
        book.ClearFrames();
        book.ClearWorks();
        return (works, frames);
    }

    void Align()
    {
        var next = Math.Max(book.NextBulk, host.Clipboard.NextBulk);
        book.NextBulk = next;
        host.Clipboard.NextBulk = next;
    }

    Frame Expand(Frame frame)
    {
        if (frame.Type != FrameTypes.FileSegment || frame.Payload.Length != 4)
        {
            return frame;
        }

        var index = BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span);
        var slot = book.Outbound(RemoteId);
        if (slot is null || !host.TryBytes(slot.TransferId, out var file))
        {
            return frame;
        }

        var offset = (long)index * slot.SegmentSize;
        if (offset >= file.Length)
        {
            return frame;
        }

        var take = (int)Math.Min(slot.SegmentSize, file.Length - offset);
        return FrameMessages.Segment(frame.StreamId, index, file.AsSpan((int)offset, take));
    }

    FileOfferPayload? DecodeOffer(Frame frame) =>
        frame.Type == FrameTypes.FileOffer
            ? ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.FileOfferPayload).Value
            : null;

    List<Frame> NewPairingFrames()
    {
        var frames = pairing.Frames.Skip(pairingSent).ToList();
        pairingSent = pairing.Frames.Count;
        return frames;
    }

    Frame Hello(string intent) =>
        FrameMessages.Json(FrameTypes.Hello, 0, 0, FrameTypes.SessionStream, new HelloPayload(intent, host.DisplayName), ProtocolJsonContext.Default.HelloPayload);

    void NoteSurvivor()
    {
        if (!pairing.IsSurvivor || entered)
        {
            return;
        }

        var first = readyTick < 0;
        if (first)
        {
            readyTick = Environment.TickCount64;
        }

        if (host.OtherLive(this))
        {
            return;
        }

        if (host.IsTrusted(RemoteId) && RemoteIntent == "trusted")
        {
            EnterTrusted();
            return;
        }

        if (first)
        {
            host.ShowPrompt(this);
        }
    }

    void Post(Frame frame) => Post([frame]);

    void Post(IReadOnlyList<Frame> frames)
    {
        if (frames.Count == 0 || closed)
        {
            return;
        }

        lock (writeGate)
        {
            foreach (var frame in frames)
            {
                outbox.Enqueue(frame);
            }
        }

        poke.Release();
    }

    void PostImmediate(IReadOnlyList<Frame> frames)
    {
        if (frames.Count == 0 || closed)
        {
            return;
        }

        lock (writeGate)
        {
            foreach (var frame in frames)
            {
                immediate.Enqueue(frame);
            }
        }

        poke.Release();
    }

    async Task WriteLoop()
    {
        var pause = int.TryParse(Environment.GetEnvironmentVariable("CONTEXTALL_SEGMENT_PAUSE_MS"), out var ms) ? ms : 0;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                await poke.WaitAsync(cancel.Token).ConfigureAwait(false);
                while (true)
                {
                    Frame frame = default!;
                    var have = false;
                    lock (writeGate)
                    {
                        if (immediate.Count > 0)
                        {
                            frame = immediate.Dequeue();
                            have = true;
                        }
                        else if (outbox.TryWriteNext(out frame))
                        {
                            have = true;
                        }
                    }

                    if (!have)
                    {
                        break;
                    }

                    if (frame.Type == FrameTypes.FileSegment && pause > 0)
                    {
                        await Task.Delay(pause, cancel.Token).ConfigureAwait(false);
                    }

                    var rented = ArrayPool<byte>.Shared.Rent(FrameLimits.HeaderBytes + frame.Payload.Length);
                    try
                    {
                        if (!FrameCodec.TryEncode(rented, frame, out var written))
                        {
                            continue;
                        }

                        await stream.WriteAsync(rented.AsMemory(0, written), cancel.Token).ConfigureAwait(false);
                        await stream.FlushAsync(cancel.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                        ReleaseSegment(frame);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or QuicException)
        {
            // The session closed.
        }
    }

    void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        cancel.Cancel();
        poke.Release();
    }

    static void ReleaseSegment(Frame frame)
    {
        // FrameMessages.Segment rents this buffer. A 4-byte segment placeholder is not rented.
        if (frame.Type != FrameTypes.FileSegment || frame.Payload.Length < 36)
        {
            return;
        }

        if (MemoryMarshal.TryGetArray(frame.Payload, out ArraySegment<byte> segment) && segment.Array is not null)
        {
            ArrayPool<byte>.Shared.Return(segment.Array);
        }
    }

    static TransferSnapshotPayload TransferView(TransferSlot slot) =>
        new(slot.TransferId, slot.PeerId, slot.Direction, slot.FileName, slot.Completed, slot.SegmentCount, slot.MoveBlocked);

    static Frame Progress(TransferSlot? slot, string? ended) =>
        slot is null
            ? Ended("", ended ?? "closed")
            : FrameMessages.Json(FrameTypes.TransferProgress, 2, 0, FrameTypes.SessionStream, TransferView(slot), ProtocolJsonContext.Default.TransferSnapshotPayload);

    static Frame Ended(string transferId, string outcome) =>
        FrameMessages.Json(FrameTypes.TransferEnded, 2, 0, FrameTypes.SessionStream, new TransferEndedPayload(transferId, outcome), ProtocolJsonContext.Default.TransferEndedPayload);
}
