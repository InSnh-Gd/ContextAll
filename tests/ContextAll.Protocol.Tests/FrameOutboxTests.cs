using ContextAll.Protocol;

namespace ContextAll.Protocol.Tests;

public sealed class FrameOutboxTests
{
    [Fact]
    public void A_queued_priority_0_frame_is_written_before_a_queued_priority_3_frame()
    {
        var outbox = new FrameOutbox();
        var bulk = new Frame(1, FrameTypes.FileSegment, 3, 0, 2, new byte[] { 1 });
        var session = new Frame(1, FrameTypes.Hello, 0, 0, 0, new byte[] { 2 });

        Assert.False(outbox.Enqueue(bulk).IsFailure);
        Assert.False(outbox.Enqueue(session).IsFailure);
        Assert.True(outbox.TryWriteNext(out var first));
        Assert.True(outbox.TryWriteNext(out var second));

        Assert.Equal(0, first.Priority);
        Assert.Equal(FrameTypes.Hello, first.Type);
        Assert.Equal(3, second.Priority);
    }
}
