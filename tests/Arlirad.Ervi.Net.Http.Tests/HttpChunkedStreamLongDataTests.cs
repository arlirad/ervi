using System.Text;

namespace Arlirad.Ervi.Net.Http.Tests;

public class HttpChunkedStreamLongDataTests
{
    private static byte[] MakeBytes(int length, byte value = (byte)'A')
    {
        var data = new byte[length];
        Array.Fill(data, value);
        return data;
    }

    [TestCase(1)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(255)]
    [TestCase(256)]
    [TestCase(4095)]
    [TestCase(4096)]
    [TestCase(65535)]
    [TestCase(65536)]
    public async Task Single_Chunk_Uses_Uppercase_Hex_Length_Without_Leading_Zeros(int size)
    {
        using var ms = new MemoryStream();

        await using (var chunked = new HttpChunkedStream(ms))
        {
            var payload = MakeBytes(size);
            chunked.Write(payload, 0, payload.Length);
        }

        var hex = size.ToString("X"); // Uppercase hex without leading zeros
        var expected = Encoding.ASCII.GetBytes($"{hex}\r\n{new string('A', size)}\r\n0\r\n\r\n");
        var actual = ms.ToArray();
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task Multiple_Large_Chunks_Are_Formatted_And_Concatenated_Correctly()
    {
        var sizes = new[] { 1024, 3000, 4096, 32, 255 };

        using var ms = new MemoryStream();

        await using (var chunked = new HttpChunkedStream(ms))
        {
            foreach (var s in sizes)
            {
                var payload = MakeBytes(s, (byte)'B');
                chunked.Write(payload, 0, payload.Length);
            }
        }

        // Build expected bytes
        using var expectedMs = new MemoryStream();

        foreach (var s in sizes)
        {
            var hex = Encoding.ASCII.GetBytes(s.ToString("X") + "\r\n");
            expectedMs.Write(hex, 0, hex.Length);
            expectedMs.Write(MakeBytes(s, (byte)'B'));
            expectedMs.Write("\r\n"u8);
        }

        expectedMs.Write("0\r\n\r\n"u8);

        Assert.That(ms.ToArray(), Is.EqualTo(expectedMs.ToArray()));
    }

    [Test]
    public async Task Async_Write_With_Large_Buffer_Formats_Length_Correctly()
    {
        const int size = 4096; // boundary where hex grows to 4 digits (1000)
        using var ms = new MemoryStream();

        await using (var chunked = new HttpChunkedStream(ms))
        {
            var payload = MakeBytes(size, (byte)'C');
            await chunked.WriteAsync(payload.AsMemory());
        }

        var expected = Encoding.ASCII.GetBytes($"1000\r\n{new string('C', size)}\r\n0\r\n\r\n");
        Assert.That(ms.ToArray(), Is.EqualTo(expected));
    }
}