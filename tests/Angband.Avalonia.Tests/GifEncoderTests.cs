using Angband.Avalonia.Services;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's GIF writer, read back by a small GIF reader of the tests' own.</summary>
public sealed class GifEncoderTests
{
    /// <summary>A GIF read back: its size, loop count, and each frame as shown (0xRRGGBB pixels) with its delay.</summary>
    internal sealed record DecodedGif(int Width, int Height, int? Loops, List<(int[] Pixels, int DelayCs)> Frames, int ColourCount);

    /// <summary>
    /// Reads a GIF as a viewer would: the global colour table, the loop extension, and each frame's
    /// graphic control (delay, see-through index) and LZW picture, drawn over the frame before.
    /// </summary>
    internal static DecodedGif Decode(byte[] data)
    {
        var at = 0;
        byte Byte() => data[at++];
        int Short() => data[at++] | data[at++] << 8;
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(data, 0, 6));
        at = 6;
        int width = Short(), height = Short();
        var flags = Byte();
        Byte();
        Byte();
        Assert.True((flags & 0x80) != 0, "a global colour table");
        var colours = 1 << ((flags & 7) + 1);
        var table = new int[colours];
        for (var i = 0; i < colours; i++) table[i] = Byte() << 16 | Byte() << 8 | Byte();
        var canvas = new int[width * height];
        var frames = new List<(int[], int)>();
        int? loops = null;
        var delay = 0;
        int? transparent = null;
        while (true)
        {
            var kind = Byte();
            if (kind == 0x3B) break;
            if (kind == 0x21)
            {
                var label = Byte();
                var blocks = new List<byte[]>();
                for (int n; (n = Byte()) != 0; at += n) blocks.Add(data[at..(at + n)]);
                if (label == 0xF9)
                {
                    var b = blocks[0];
                    Assert.Equal(1, (b[0] >> 2) & 7); // don't dispose: the next frame draws over this one
                    delay = b[1] | b[2] << 8;
                    transparent = (b[0] & 1) != 0 ? b[3] : null;
                }
                else if (label == 0xFF && System.Text.Encoding.ASCII.GetString(blocks[0]) == "NETSCAPE2.0")
                    loops = blocks[1][1] | blocks[1][2] << 8;
                continue;
            }
            Assert.Equal(0x2C, kind);
            int left = Short(), top = Short(), w = Short(), h = Short();
            Assert.Equal(0, Byte() & 0x80); // no local table
            Assert.True(left + w <= width && top + h <= height, "the frame lies inside the picture");
            var minCodeSize = Byte();
            var stream = new List<byte>();
            for (int n; (n = Byte()) != 0; at += n)
            {
                Assert.True(n <= 255);
                stream.AddRange(data[at..(at + n)]);
            }
            var indices = Lzw([.. stream], minCodeSize, w * h);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var index = indices[y * w + x];
                if (index == transparent) continue;
                canvas[(top + y) * width + left + x] = table[index];
            }
            frames.Add(((int[])canvas.Clone(), delay));
            transparent = null;
        }
        return new DecodedGif(width, height, loops, frames, colours);
    }

    private static byte[] Lzw(byte[] data, int minCodeSize, int count)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var output = new List<byte>(count);
        var table = new List<byte[]>();
        void Reset()
        {
            table.Clear();
            for (var i = 0; i < clear; i++) table.Add([(byte)i]);
            table.Add([]);
            table.Add([]);
        }
        Reset();
        var codeSize = minCodeSize + 1;
        var bit = 0;
        byte[]? previous = null;
        while (true)
        {
            var code = 0;
            for (var i = 0; i < codeSize; i++, bit++)
                if ((data[bit >> 3] >> (bit & 7) & 1) != 0) code |= 1 << i;
            if (code == clear)
            {
                Reset();
                codeSize = minCodeSize + 1;
                previous = null;
                continue;
            }
            if (code == end) break;
            byte[] entry;
            if (code < table.Count) entry = table[code];
            else
            {
                Assert.Equal(table.Count, code); // (the one code that may come before it is made)
                entry = [.. previous!, previous![0]];
            }
            output.AddRange(entry);
            if (previous is not null && table.Count < 4096)
            {
                table.Add([.. previous, entry[0]]);
                if (table.Count == 1 << codeSize && codeSize < 12) codeSize++;
            }
            previous = entry;
        }
        Assert.Equal(count, output.Count);
        return [.. output];
    }

    private static byte[] Frame(int w, int h, Func<int, int, int> rgb)
    {
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = rgb(x, y);
            var i = (y * w + x) * 4;
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = ((byte)c, (byte)(c >> 8), (byte)(c >> 16), 255);
        }
        return bgra;
    }

    [Fact]
    public void FewColours_AreKeptExactly_AndEachFrameReadsBackAsDrawn()
    {
        int[] colours = [0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF];
        var gif = new GifEncoder(20, 12);
        var frames = new List<int[]>();
        for (var f = 0; f < 4; f++)
        {
            var f1 = f;
            // a square that moves across a striped ground
            int Pixel(int x, int y) => x >= f1 * 4 && x < f1 * 4 + 4 && y >= 4 && y < 8 ? colours[2 + f1 % 3] : colours[(x / 5 + y) % 2];
            gif.AddFrame(Frame(20, 12, Pixel), 20 * 4, 150);
            frames.Add([.. Enumerable.Range(0, 240).Select(i => Pixel(i % 20, i / 20))]);
        }
        var bytes = gif.ToArray();
        var read = Decode(bytes);
        Assert.Equal((20, 12), (read.Width, read.Height));
        Assert.Equal(0, read.Loops); // for ever
        Assert.Equal(4, read.Frames.Count);
        for (var f = 0; f < 4; f++)
        {
            Assert.Equal(frames[f], read.Frames[f].Pixels);
            Assert.Equal(15, read.Frames[f].DelayCs);
        }
        Assert.True(read.ColourCount <= 8);
    }

    [Fact]
    public void AFrameLikeTheOneBefore_HoldsThatOneLonger_AndHoldLengthensTheLast()
    {
        var gif = new GifEncoder(8, 8);
        var still = Frame(8, 8, (x, y) => (x + y) % 2 == 0 ? 0xF8F8F8 : 0);
        gif.AddFrame(still, 32, 100);
        gif.AddFrame(still, 32, 100);
        gif.AddFrame(Frame(8, 8, (_, _) => 0xF80000), 32, 100);
        gif.Hold(2000);
        var read = Decode(gif.ToArray());
        Assert.Equal([20, 210], read.Frames.Select(f => f.DelayCs));
    }

    [Fact]
    public void ManyColours_ComeDownTo256_NearTheOriginals_AndLongPicturesCompress()
    {
        // A smooth gradient (thousands of colours) and some noise: LZW's table fills and clears many times.
        const int w = 320, h = 200;
        var rng = new Random(5);
        var noise = Enumerable.Range(0, w * h).Select(_ => rng.Next(4)).ToArray();
        int Pixel(int x, int y) => (x * 255 / w) << 16 | (y * 255 / h) << 8 | ((x + y) * 255 / (w + h)) ^ noise[y * w + x];
        var gif = new GifEncoder(w, h);
        gif.AddFrame(Frame(w, h, Pixel), w * 4, 100);
        gif.AddFrame(Frame(w, h, (x, y) => x is > 100 and < 140 && y is > 50 and < 90 ? 0xFFFFFF : Pixel(x, y)), w * 4, 100);
        var bytes = gif.ToArray();
        var read = Decode(bytes);
        Assert.Equal(256, read.ColourCount);
        Assert.Equal(2, read.Frames.Count);
        var worst = 0;
        for (var i = 0; i < w * h; i++)
        {
            int a = read.Frames[0].Pixels[i], b = Pixel(i % w, i / w);
            worst = Math.Max(worst, Math.Max(Math.Abs((a >> 16) - (b >> 16)), Math.Max(Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)), Math.Abs((a & 255) - (b & 255)))));
        }
        Assert.True(worst <= 48, $"every colour near its own (worst off by {worst})");
        Assert.Equal(0xFFFFFF, read.Frames[1].Pixels[70 * w + 120]);
        Assert.Equal(read.Frames[0].Pixels[10 * w + 10], read.Frames[1].Pixels[10 * w + 10]); // (unchanged, left see-through)
        Assert.True(bytes.Length < w * h * 2, $"compressed ({bytes.Length:N0} bytes)");
    }
}
