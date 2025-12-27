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
    
    private int _chunkRemaining;
    private bool _eof;

    public override void Flush()
    {
        inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_eof)
            return 0;

        if (_chunkRemaining == 0)
        {
            _chunkRemaining = ReadChunkLength();
            if (_chunkRemaining == 0)
            {
                _eof = true;
                ReadNewLine();
                return 0;
            }
        }

        var toRead = Math.Min(count, _chunkRemaining);
        var read = inner.Read(buffer, offset, toRead);

        _chunkRemaining -= read;

        if (_chunkRemaining == 0)
            ReadNewLine();

        return read;
    }

    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_eof)
            return 0;

        if (_chunkRemaining == 0)
        {
            _chunkRemaining = await ReadChunkLengthAsync(cancellationToken);
            if (_chunkRemaining == 0)
            {
                _eof = true;
                await ReadNewLineAsync(cancellationToken);
                
                return 0;
            }
        }

        var toRead = Math.Min(buffer.Length, _chunkRemaining);
        var read = await inner.ReadAsync(buffer[..toRead], cancellationToken);

        _chunkRemaining -= read;

        if (_chunkRemaining == 0)
            await ReadNewLineAsync(cancellationToken);

        return read;
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
    
    private int ReadChunkLength()
    {
        var line = ReadLine();
        var separatorIndex = line.IndexOf(';');
        if (separatorIndex != -1)
            line = line[..separatorIndex];

        return int.Parse(line.Trim(), System.Globalization.NumberStyles.HexNumber);
    }

    private async ValueTask<int> ReadChunkLengthAsync(CancellationToken ct)
    {
        var line = await ReadLineAsync(ct);
        var separatorIndex = line.IndexOf(';');
        if (separatorIndex != -1)
            line = line[..separatorIndex];

        return int.Parse(line.Trim(), System.Globalization.NumberStyles.HexNumber);
    }

    private string ReadLine()
    {
        var sb = new System.Text.StringBuilder();
        int b;
        
        while ((b = inner.ReadByte()) != -1)
        {
            if (b == '\r')
            {
                var next = inner.ReadByte();
                if (next == '\n')
                    break;
                sb.Append((char)b);
                if (next != -1) sb.Append((char)next);
                continue;
            }
            sb.Append((char)b);
        }
        
        return sb.ToString();
    }

    private async ValueTask<string> ReadLineAsync(CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        var buffer = new byte[1];
        
        while (await inner.ReadAsync(buffer, ct) > 0)
        {
            var b = buffer[0];
            if (b == '\r')
            {
                if (await inner.ReadAsync(buffer, ct) > 0 && buffer[0] == '\n')
                    break;
                sb.Append('\r');
                if (buffer[0] != '\n') sb.Append((char)buffer[0]);
                continue;
            }
            
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    private void ReadNewLine()
    {
        var b1 = inner.ReadByte();
        var b2 = inner.ReadByte();
        
        if (b1 != '\r' || b2 != '\n')
            throw new IOException("Invalid chunked encoding: expected CRLF");
    }

    private async ValueTask ReadNewLineAsync(CancellationToken ct)
    {
        var buffer = new byte[2];
        var read = 0;
        
        while (read < 2)
        {
            var r = await inner.ReadAsync(buffer.AsMemory(read), ct);
            if (r == 0)
                throw new IOException("Unexpected end of stream");
            
            read += r;
        }
        
        if (buffer[0] != '\r' || buffer[1] != '\n')
            throw new IOException("Invalid chunked encoding: expected CRLF");
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