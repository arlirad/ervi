using System.Text;

namespace Arlirad.Ervi.Net.Http.Tests;

public class HttpChunkedStreamTests
{
    [Test]
    public async Task Writes_Chunked_Format_For_Two_Chunks_And_Terminator_On_Dispose()
    {
        using var ms = new MemoryStream();

        await using (var chunked = new HttpChunkedStream(ms))
        {
            var a = Encoding.ASCII.GetBytes("Hello");
            var b = Encoding.ASCII.GetBytes("World!");

            // 5 and 6 bytes respectively
            chunked.Write(a, 0, a.Length);
            chunked.Write(b, 0, b.Length);
        }

        var text = Encoding.ASCII.GetString(ms.ToArray());
        // Expected: 5\r\nHello\r\n6\r\nWorld!\r\n0\r\n\r\n
        Assert.That(text, Is.EqualTo("5\r\nHello\r\n6\r\nWorld!\r\n0\r\n\r\n"));
    }

    [Test]
    public async Task Zero_Length_Write_Does_Not_End_Stream()
    {
        using var ms = new MemoryStream();

        await using (var chunked = new HttpChunkedStream(ms))
        {
            var a = Encoding.ASCII.GetBytes("ABC");
            chunked.Write(a, 0, a.Length);
            // Should be a no-op
            chunked.Write(a, 0, 0);
        }

        var text = Encoding.ASCII.GetString(ms.ToArray());
        Assert.That(text, Is.EqualTo("3\r\nABC\r\n0\r\n\r\n"));
    }

    [Test]
    public async Task Async_Write_Does_Not_Write_For_Empty_Buffer()
    {
        using var ms = new MemoryStream();

        await using (var chunked = new HttpChunkedStream(ms))
        {
            await chunked.WriteAsync(ReadOnlyMemory<byte>.Empty);
            await chunked.WriteAsync(Encoding.ASCII.GetBytes("X").AsMemory());
        }

        var text = Encoding.ASCII.GetString(ms.ToArray());
        Assert.That(text, Is.EqualTo("1\r\nX\r\n0\r\n\r\n"));
    }

    [Test]
    public async Task Reads_Chunked_Format()
    {
        var data = Encoding.ASCII.GetBytes("5\r\nHello\r\n6\r\nWorld!\r\n0\r\n\r\n");
        using var ms = new MemoryStream(data);
        using var chunked = new HttpChunkedStream(ms);

        var buffer = new byte[100];
        var read = await chunked.ReadAsync(buffer);
        Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("Hello"));

        read = await chunked.ReadAsync(buffer);
        Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("World!"));

        read = await chunked.ReadAsync(buffer);
        Assert.That(read, Is.EqualTo(0));
    }

    [Test]
    public void Synchronous_Read_Works()
    {
        var data = Encoding.ASCII.GetBytes("5\r\nHello\r\n0\r\n\r\n");
        using var ms = new MemoryStream(data);
        using var chunked = new HttpChunkedStream(ms);

        var buffer = new byte[100];
        var read = chunked.Read(buffer, 0, buffer.Length);
        Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("Hello"));

        read = chunked.Read(buffer, 0, buffer.Length);
        Assert.That(read, Is.EqualTo(0));
    }

    [Test]
    public async Task Read_Partial_Chunks()
    {
        var data = Encoding.ASCII.GetBytes("5\r\nHello\r\n0\r\n\r\n");
        using var ms = new MemoryStream(data);
        using var chunked = new HttpChunkedStream(ms);

        var buffer = new byte[3];
        var read = await chunked.ReadAsync(buffer);
        Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("Hel"));

        read = await chunked.ReadAsync(buffer);
        Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("lo"));

        read = await chunked.ReadAsync(buffer);
        Assert.That(read, Is.EqualTo(0));
    }

    [Test]
    public async Task Handles_Chunk_Extensions()
    {
        var data = Encoding.ASCII.GetBytes("5;ext=val\r\nHello\r\n0\r\n\r\n");
        using var ms = new MemoryStream(data);
        using var chunked = new HttpChunkedStream(ms);

        var buffer = new byte[100];
        var read = await chunked.ReadAsync(buffer);
        Assert.That(Encoding.ASCII.GetString(buffer, 0, read), Is.EqualTo("Hello"));

        read = await chunked.ReadAsync(buffer);
        Assert.That(read, Is.EqualTo(0));
    }
}