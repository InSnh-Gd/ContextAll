using System.Threading.Channels;
using ContextAll.Protocol;

namespace ContextAll.Host;

public sealed class FrontClient
{
    readonly Channel<byte[]> outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public HashSet<string> Requests { get; } = [];

    public void Post(Frame frame)
    {
        var bytes = FrameMessages.Encode(frame);
        if (bytes.Length > 0)
        {
            outgoing.Writer.TryWrite(bytes);
        }
    }

    public async Task CopyTo(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var bytes in outgoing.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // The front end left.
        }
    }

    public void Complete() => outgoing.Writer.TryComplete();
}
