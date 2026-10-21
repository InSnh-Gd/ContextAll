namespace ContextAll.Protocol;

public sealed class FrameOutbox
{
    readonly Queue<Frame>[] lanes =
    [
        new Queue<Frame>(),
        new Queue<Frame>(),
        new Queue<Frame>(),
        new Queue<Frame>(),
    ];

    public FrameOutcome<Frame> Enqueue(Frame frame)
    {
        if (frame.Priority > FrameTypes.LowestPriority)
        {
            return FrameOutcome<Frame>.Failure("priority");
        }

        lanes[frame.Priority].Enqueue(frame);
        return FrameOutcome<Frame>.Success(frame);
    }

    public bool TryWriteNext(out Frame frame)
    {
        for (var priority = 0; priority < lanes.Length; priority++)
        {
            if (lanes[priority].Count > 0)
            {
                frame = lanes[priority].Dequeue();
                return true;
            }
        }

        frame = new Frame(0, 0, 0, 0, 0, ReadOnlyMemory<byte>.Empty);
        return false;
    }
}
