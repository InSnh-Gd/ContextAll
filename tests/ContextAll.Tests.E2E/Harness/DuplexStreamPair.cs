using System.IO.Pipelines;

namespace ContextAll.Tests.E2E.Harness;

public static class DuplexStreamPair
{
    public static (Stream SideA, Stream SideB) Create()
    {
        var pipeAtoB = new Pipe();
        var pipeBtoA = new Pipe();

        var streamA = new PairedStream(pipeBtoA.Reader.AsStream(), pipeAtoB.Writer.AsStream());
        var streamB = new PairedStream(pipeAtoB.Reader.AsStream(), pipeBtoA.Writer.AsStream());

        return (streamA, streamB);
    }

    private sealed class PairedStream : Stream
    {
        readonly Stream readStream;
        readonly Stream writeStream;

        public PairedStream(Stream readStream, Stream writeStream)
        {
            this.readStream = readStream;
            this.writeStream = writeStream;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => writeStream.Flush();
        public override Task FlushAsync(CancellationToken ct) => writeStream.FlushAsync(ct);

        public override int Read(byte[] buffer, int offset, int count) => readStream.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            readStream.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            readStream.ReadAsync(buffer, ct);

        public override void Write(byte[] buffer, int offset, int count) => writeStream.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            writeStream.WriteAsync(buffer, offset, count, ct);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            writeStream.WriteAsync(buffer, ct);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                writeStream.Dispose();
                readStream.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await writeStream.DisposeAsync().ConfigureAwait(false);
            await readStream.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
