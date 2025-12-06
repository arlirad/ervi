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
}