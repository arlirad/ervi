namespace Arlirad.Ervi.Net.Http;

public class HttpChunkedStream(Stream inner) : Stream
{
    private const int LengthBufferSize = 8;
    private static readonly byte[] NewLine = "\r\n"u8.ToArray();

    public override bool CanRead { get => inner.CanRead; }
    public override bool CanSeek { get => false; }
    public override bool CanWrite { get => inner.CanWrite; }
    public override long Length { get => throw new NotSupportedException(); }
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int length)
    {
        if (length == 0)
            return;

        WriteChunk(buffer, offset, length);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        return buffer.Length == 0
            ? ValueTask.CompletedTask
            : WriteChunkAsync(buffer, ct);
    }

    public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
            return;

        await WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
    }

    public async override ValueTask DisposeAsync()
    {
        await WriteChunkAsync(null);
        await base.DisposeAsync();
    }

    private void WriteChunk(byte[]? buffer, int offset, int count)
    {
        WriteLength(count);

        if (buffer is not null)
            inner.Write(buffer!, offset, count);

        inner.Write(NewLine);
    }

    private void WriteLength(int bufferLength)
    {
        var lengthBuffer = new byte[LengthBufferSize];
        var length = LengthToBytes(lengthBuffer, bufferLength);

        inner.Write(lengthBuffer, 0, length);
        inner.Write(NewLine);
    }

    private async ValueTask WriteChunkAsync(ReadOnlyMemory<byte>? buffer, CancellationToken ct = default)
    {
        await WriteLengthAsync(buffer?.Length ?? 0, ct);

        if (buffer.HasValue)
            await inner.WriteAsync(buffer.Value, ct);

        await inner.WriteAsync(NewLine, ct);
    }

    private async ValueTask WriteLengthAsync(int bufferLength, CancellationToken ct)
    {
        var lengthBuffer = new byte[LengthBufferSize];
        var length = LengthToBytes(lengthBuffer, bufferLength);

        await inner.WriteAsync(lengthBuffer.AsMemory(0, length), ct);
        await inner.WriteAsync(NewLine, ct);
    }

    private static int LengthToBytes(byte[] buffer, int value)
    {
        if (value == 0)
        {
            buffer[0] = (byte)'0';
            return 1;
        }

        var pos = buffer.Length;

        while (value > 0)
        {
            var nibble = value % 16;
            value /= 16;
            buffer[--pos] = (byte)((nibble < 10) ? ('0' + nibble) : ('A' + (nibble - 10)));
        }

        var len = buffer.Length - pos;

        buffer[pos..].CopyTo(buffer.AsSpan());

        return len;
    }
}