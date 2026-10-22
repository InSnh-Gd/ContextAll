using System.Text;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Host.Tests;

public sealed class UntrustedSessionTests
{
    [Fact]
    public void An_unpaired_peer_cannot_prompt_or_resume_a_download()
    {
        using var fixture = new Fixture();
        var link = fixture.Link();
        fixture.Host.Track(link);
        fixture.Plant(link.RemoteId);

        link.Dispatch(Resume("{\"comment\":\"done\"}"));
        link.Dispatch(Resume("{\"done\":true}"));
        var offered = link.Dispatch(Offer());
        var prompts = new List<FilePromptSnapshotPayload>();
        var transfers = new List<TransferSnapshotPayload>();
        link.Collect(prompts, transfers);

        Assert.Empty(offered.Events);
        Assert.Empty(prompts);
        Assert.Empty(transfers);
        Assert.NotEmpty(Directory.GetFiles(fixture.Transfers, "*.meta.json"));
        Assert.Empty(Directory.GetFiles(fixture.Host.Downloads));
    }

    [Fact]
    public void A_trusted_session_accepts_only_a_done_payload_and_unpair_clears_the_partial()
    {
        using var fixture = new Fixture();
        var link = fixture.Link();
        fixture.Host.Track(link);
        fixture.Host.SaveTrust(new TrustEntry(link.RemoteId, "peer", Convert.ToBase64String(fixture.RemoteSpki), false, true));
        fixture.Plant(link.RemoteId);
        link.MarkWritable();
        link.Dispatch(FrameMessages.Json(
            FrameTypes.Hello,
            0,
            0,
            FrameTypes.SessionStream,
            new HelloPayload("trusted", "peer"),
            ProtocolJsonContext.Default.HelloPayload));
        Assert.True(link.Entered);

        link.Dispatch(Resume("{\"comment\":\"done\"}"));
        var early = link.Dispatch(Offer());
        var prompts = new List<FilePromptSnapshotPayload>();
        var transfers = new List<TransferSnapshotPayload>();
        link.Collect(prompts, transfers);
        Assert.Empty(early.Events);
        Assert.Empty(prompts);

        link.Dispatch(Resume("{\"done\":true}"));
        var prompted = link.Dispatch(Offer());
        prompts.Clear();
        link.Collect(prompts, transfers);
        Assert.Contains(prompted.Events, frame => frame.Type == FrameTypes.FilePrompt);
        Assert.NotEmpty(prompts);

        prompts.Clear();
        transfers.Clear();
        link.Dispatch(PlantedResume());
        link.Collect(prompts, transfers);
        Assert.Empty(prompts);
        Assert.Contains(transfers, slot => slot.TransferId == PlantedId);

        fixture.Host.RemoveTrust(link.RemoteId);
        prompts.Clear();
        transfers.Clear();
        link.Collect(prompts, transfers);
        Assert.Empty(transfers);
        Assert.Empty(Directory.GetFiles(fixture.Transfers, "*.meta.json"));

        var again = fixture.Link();
        again.Dispatch(PlantedResume());
        again.Collect(prompts, transfers);
        Assert.Empty(prompts);
        Assert.Empty(transfers);
        Assert.Empty(Directory.GetFiles(fixture.Host.Downloads));
    }

    [Fact]
    public void A_segment_from_another_stream_cannot_write_the_active_receive()
    {
        using var fixture = new Fixture();
        var link = fixture.Link();
        fixture.Host.Track(link);
        fixture.Host.SaveTrust(new TrustEntry(link.RemoteId, "peer", Convert.ToBase64String(fixture.RemoteSpki), false, true));
        fixture.Plant(link.RemoteId);
        link.MarkWritable();
        link.Dispatch(FrameMessages.Json(FrameTypes.Hello, 0, 0, FrameTypes.SessionStream,
            new HelloPayload("trusted", "peer"), ProtocolJsonContext.Default.HelloPayload));
        link.Dispatch(PlantedResume());
        byte[] data = [1, 2, 3, 4];
        var payload = new byte[36 + data.Length];
        System.Security.Cryptography.SHA256.HashData(data).CopyTo(payload, 4);
        data.CopyTo(payload, 36);
        var segment = new Frame(payload.Length, FrameTypes.FileSegment, 3, 0, 5, payload);
        link.Dispatch(segment);

        Assert.Equal(new byte[4], fixture.Host.Store.Read(PlantedId));
        Assert.Equal(0, fixture.Host.Store.Load(PlantedId)?.Completed);
        Assert.Empty(Directory.GetFiles(fixture.Host.Downloads));
    }

    static Frame Resume(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return new Frame(body.Length, FrameTypes.FileResume, 2, 0, FrameTypes.SessionStream, body);
    }

    static Frame PlantedResume() =>
        FrameMessages.Json(
            FrameTypes.FileResume,
            2,
            0,
            FrameTypes.SessionStream,
            new FileResumePayload(PlantedId, 4),
            ProtocolJsonContext.Default.FileResumePayload);

    static Frame Offer() =>
        FrameMessages.Json(
            FrameTypes.FileOffer,
            2,
            0,
            FrameTypes.SessionStream,
            new FileOfferPayload(new string('c', 32), 2, 4, TransferBook.SegmentSize, 1, new string('d', 64), "note.txt"),
            ProtocolJsonContext.Default.FileOfferPayload);

    const string PlantedId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    sealed class Fixture : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "contextall-untrusted-" + Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(root);
            RemoteSpki = Enumerable.Range(1, 91).Select(i => (byte)i).ToArray();
            Host = LanHost.CreateIsolated(root);
            Transfers = Path.Combine(root, "state", "transfers");
        }

        public LanHost Host { get; }

        public byte[] RemoteSpki { get; }

        public string Transfers { get; }

        public PeerLink Link() => new(Host, null!, null!, RemoteSpki, initiated: false);

        public void Plant(string peerId)
        {
            Host.Store.Create(new TransferSlot
            {
                TransferId = PlantedId,
                PeerId = peerId,
                Direction = "receive",
                FileName = "secret.txt",
                TotalSize = 4,
                SegmentSize = TransferBook.SegmentSize,
                SegmentCount = 1,
                RootSha256 = new string('b', 64),
                Bits = new byte[1],
                Completed = 0,
            }, null);
        }

        public void Dispose()
        {
            Host.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
