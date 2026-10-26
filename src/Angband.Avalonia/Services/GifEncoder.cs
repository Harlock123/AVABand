namespace Angband.Avalonia.Services;

/// <summary>
/// AVABand's own animated GIF writer, for clips: no libraries, nothing to install. Frames are added as
/// 32-bit BGRA pixels and kept as 15-bit colour (which 256 colours can't tell from 24 anyway); on
/// writing, one palette is made for them all by median cut over every pixel added, each frame after
/// the first is cut down to the rectangle that changed (what stayed the same is left see-through, so
/// the frame before shows), and the pictures are LZW-compressed as GIF89a wants. It loops forever by
/// default (the NETSCAPE2.0 extension), and each frame has its own delay.
/// </summary>
public sealed class GifEncoder
{
    private readonly List<(ushort[] Pixels, int DelayCs)> _frames = [];
    private readonly int[] _histogram = new int[1 << 15];

    public GifEncoder(int width, int height)
    {
        if (width is < 1 or > 65535 || height is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(width), "A GIF is 1 to 65535 pixels a side.");
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int FrameCount => _frames.Count;

    /// <summary>How many times the animation plays: 0 (the default) for ever, as clips do.</summary>
    public int Loops { get; set; }

    /// <summary>
    /// Adds a frame: <see cref="Width"/> x <see cref="Height"/> pixels of 4 bytes each, blue, green, red,
    /// alpha (alpha is ignored: a GIF frame is opaque here), rows <paramref name="stride"/> bytes apart.
    /// </summary>
    /// <param name="delayMs">How long it is shown (GIF counts in hundredths of a second; at least 20 ms, as browsers want).</param>
    public void AddFrame(ReadOnlySpan<byte> bgra, int stride, int delayMs)
    {
        if (stride < Width * 4 || bgra.Length < stride * (Height - 1) + Width * 4)
            throw new ArgumentException("The pixels don't cover the frame.", nameof(bgra));
        var pixels = new ushort[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            var row = bgra.Slice(y * stride, Width * 4);
            for (var x = 0; x < Width; x++)
            {
                var c = (ushort)(((row[x * 4 + 2] >> 3) << 10) | ((row[x * 4 + 1] >> 3) << 5) | (row[x * 4] >> 3));
                pixels[y * Width + x] = c;
                _histogram[c]++;
            }
        }
        _frames.Add((pixels, Math.Max(2, (int)Math.Round(delayMs / 10.0))));
    }

    /// <summary>Lengthens the last frame's showing (a held ending), in milliseconds.</summary>
    public void Hold(int ms)
    {
        if (_frames.Count == 0) return;
        var (pixels, delay) = _frames[^1];
        _frames[^1] = (pixels, delay + Math.Max(0, (int)Math.Round(ms / 10.0)));
    }

    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        Write(stream);
        return stream.ToArray();
    }

    public void Write(Stream output)
    {
        if (_frames.Count == 0) throw new InvalidOperationException("A GIF needs at least one frame.");
        var (palette, lookup) = Quantize(_histogram, MaxColours);
        var transparent = palette.Count; // (one past the colours: there is always room, as there are at most 255)
        var tableBits = 1;
        while (1 << tableBits < palette.Count + 1) tableBits++;
        var minCodeSize = Math.Max(2, tableBits);

        using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("GIF89a"u8);
        w.Write((ushort)Width);
        w.Write((ushort)Height);
        w.Write((byte)(0x80 | ((tableBits - 1) << 4) | (tableBits - 1))); // a global colour table, its size
        w.Write((byte)0); // background colour
        w.Write((byte)0); // pixel aspect ratio: square
        for (var i = 0; i < 1 << tableBits; i++)
        {
            var c = i < palette.Count ? palette[i] : 0;
            w.Write((byte)(c >> 16));
            w.Write((byte)(c >> 8));
            w.Write((byte)c);
        }
        // NETSCAPE2.0: loop.
        w.Write([0x21, 0xFF, 11]);
        w.Write("NETSCAPE2.0"u8);
        w.Write([3, 1]);
        w.Write((ushort)Math.Clamp(Loops, 0, 65535));
        w.Write((byte)0);

        byte[]? previous = null;
        var current = new byte[Width * Height];
        var pending = (Rect: default((int X, int Y, int W, int H)), Data: (byte[]?)null, Delay: 0, Transparent: false);
        foreach (var (pixels, delay) in _frames)
        {
            for (var i = 0; i < current.Length; i++) current[i] = lookup[pixels[i]];
            if (previous is null)
            {
                pending = ((0, 0, Width, Height), (byte[])current.Clone(), delay, false);
                previous = (byte[])current.Clone();
                continue;
            }
            // Only what changed since the frame before is written; a frame with no change just holds that one longer.
            if (Changed(previous, current) is not { } rect)
            {
                pending.Delay += delay;
                continue;
            }
            WriteFrame(w, pending.Rect, pending.Data!, pending.Delay, pending.Transparent, transparent, minCodeSize);
            var data = new byte[rect.W * rect.H];
            for (var y = 0; y < rect.H; y++)
            for (var x = 0; x < rect.W; x++)
            {
                var at = (rect.Y + y) * Width + rect.X + x;
                data[y * rect.W + x] = current[at] == previous[at] ? (byte)transparent : current[at];
            }
            pending = (rect, data, delay, true);
            (previous, current) = (current, previous);
        }
        WriteFrame(w, pending.Rect, pending.Data!, pending.Delay, pending.Transparent, transparent, minCodeSize);
        w.Write((byte)0x3B); // trailer
    }

    /// <summary>The most colours a clip's palette has: 256, less one kept for "see-through".</summary>
    public const int MaxColours = 255;

    /// <summary>The smallest rectangle holding every pixel that differs, or null if none does.</summary>
    private (int X, int Y, int W, int H)? Changed(byte[] a, byte[] b)
    {
        int left = Width, right = -1, top = Height, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            if (a.AsSpan(row, Width).SequenceEqual(b.AsSpan(row, Width))) continue;
            if (top == Height) top = y;
            bottom = y;
            for (var x = 0; x < left; x++)
                if (a[row + x] != b[row + x]) { left = x; break; }
            for (var x = Width - 1; x > right; x--)
                if (a[row + x] != b[row + x]) { right = x; break; }
        }
        return bottom < 0 ? null : (left, top, right - left + 1, bottom - top + 1);
    }

    private static void WriteFrame(BinaryWriter w, (int X, int Y, int W, int H) rect, byte[] indices, int delayCs, bool transparent,
        int transparentIndex, int minCodeSize)
    {
        // Graphic control: don't dispose (the next frame draws over this one), the delay, see-through if used.
        w.Write([0x21, 0xF9, 4, (byte)((1 << 2) | (transparent ? 1 : 0))]);
        w.Write((ushort)Math.Clamp(delayCs, 0, 65535));
        w.Write((byte)(transparent ? transparentIndex : 0));
        w.Write((byte)0);
        w.Write((byte)0x2C);
        w.Write((ushort)rect.X);
        w.Write((ushort)rect.Y);
        w.Write((ushort)rect.W);
        w.Write((ushort)rect.H);
        w.Write((byte)0); // no local colour table, not interlaced
        w.Write((byte)minCodeSize);
        Lzw(indices, minCodeSize, w);
    }

    /// <summary>
    /// GIF's LZW: variable-width codes (up to 12 bits) packed low bit first into blocks of at most 255
    /// bytes. The string table is an open-addressed hash (as the classic encoders have it), so each
    /// pixel costs a lookup or two; when it fills, a clear code starts it again.
    /// </summary>
    private static void Lzw(byte[] pixels, int minCodeSize, BinaryWriter w)
    {
        const int MaxCode = 4096;
        const int HashSize = 5003; // prime, about 80% full at most
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var keys = new int[HashSize];
        var codes = new short[HashSize];
        Array.Fill(keys, -1);
        var codeSize = minCodeSize + 1;
        var next = end + 1;

        var block = new byte[255];
        var blockLength = 0;
        var bits = 0;
        var bitCount = 0;

        void Put(int code)
        {
            bits |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                block[blockLength++] = (byte)bits;
                bits >>= 8;
                bitCount -= 8;
                if (blockLength == 255) Flush();
            }
        }

        void Flush()
        {
            if (blockLength == 0) return;
            w.Write((byte)blockLength);
            w.Write(block, 0, blockLength);
            blockLength = 0;
        }

        // A code is written at the width the table has reached; then, if the next code to be made
        // needs another bit, the width grows (the decoder, one code behind, grows at the same point).
        void Emit(int code)
        {
            Put(code);
            if (next >= 1 << codeSize && codeSize < 12) codeSize++;
        }

        Emit(clear);
        int prefix = pixels[0];
        for (var i = 1; i < pixels.Length; i++)
        {
            int k = pixels[i];
            var key = (prefix << 8) | k;
            var h = (int)((uint)key * 2654435761u % HashSize);
            var found = false;
            while (keys[h] != -1)
            {
                if (keys[h] == key)
                {
                    prefix = codes[h];
                    found = true;
                    break;
                }
                if (++h == HashSize) h = 0;
            }
            if (found) continue;
            Emit(prefix);
            if (next < MaxCode)
            {
                keys[h] = key;
                codes[h] = (short)next++;
            }
            else
            {
                Emit(clear);
                Array.Fill(keys, -1);
                codeSize = minCodeSize + 1;
                next = end + 1;
            }
            prefix = k;
        }
        Emit(prefix);
        Put(end);
        if (bitCount > 0) block[blockLength++] = (byte)bits;
        Flush();
        w.Write((byte)0); // the end of the picture's blocks
    }

    /// <summary>
    /// Median cut: the colours used (15-bit, counted) are boxed, and the box holding the most of them
    /// spread over the widest range is split at its middle, until there are <paramref name="max"/>
    /// boxes; each box's colour is the average of what it holds. Few enough colours are kept exactly.
    /// Returns the palette (0xRRGGBB) and each 15-bit colour's index in it.
    /// </summary>
    internal static (List<int> Palette, byte[] Lookup) Quantize(int[] histogram, int max)
    {
        var used = new List<int>();
        for (var c = 0; c < histogram.Length; c++)
            if (histogram[c] > 0) used.Add(c);
        var lookup = new byte[1 << 15];
        var palette = new List<int>();
        if (used.Count <= max)
        {
            foreach (var c in used)
            {
                lookup[c] = (byte)palette.Count;
                palette.Add((Expand(c >> 10) << 16) | (Expand((c >> 5) & 31) << 8) | Expand(c & 31));
            }
            if (palette.Count == 0) palette.Add(0);
            return (palette, lookup);
        }

        var boxes = new List<List<int>> { used };
        while (boxes.Count < max)
        {
            List<int>? best = null;
            var bestScore = 0L;
            foreach (var box in boxes)
            {
                if (box.Count < 2) continue;
                var score = (long)Range(box).Width * box.Sum(c => (long)histogram[c]);
                if (score > bestScore) (best, bestScore) = (box, score);
            }
            if (best is null) break;
            var axis = Range(best).Axis;
            best.Sort((a, b) => Channel(a, axis).CompareTo(Channel(b, axis)));
            var total = best.Sum(c => (long)histogram[c]);
            var half = 0L;
            var cut = 1;
            for (; cut < best.Count - 1; cut++)
            {
                half += histogram[best[cut - 1]];
                if (half * 2 >= total) break;
            }
            boxes.Remove(best);
            boxes.Add(best.GetRange(0, cut));
            boxes.Add(best.GetRange(cut, best.Count - cut));
        }
        foreach (var box in boxes)
        {
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var c in box)
            {
                var count = histogram[c];
                r += Expand(c >> 10) * (long)count;
                g += Expand((c >> 5) & 31) * (long)count;
                b += Expand(c & 31) * (long)count;
                n += count;
            }
            palette.Add((int)(r / n) << 16 | (int)(g / n) << 8 | (int)(b / n));
        }
        // Every colour used goes to its nearest in the palette (not just its own box's: that is closer, now and then).
        foreach (var c in used)
        {
            int cr = Expand(c >> 10), cg = Expand((c >> 5) & 31), cb = Expand(c & 31);
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < palette.Count; i++)
            {
                int dr = cr - (palette[i] >> 16), dg = cg - ((palette[i] >> 8) & 0xFF), db = cb - (palette[i] & 0xFF);
                var d = 3 * dr * dr + 4 * dg * dg + 2 * db * db; // (green counts for most to the eye, blue for least)
                if (d >= bestDistance) continue;
                bestDistance = d;
                bestIndex = i;
            }
            lookup[c] = (byte)bestIndex;
        }
        return (palette, lookup);
    }

    private static int Expand(int five) => (five << 3) | (five >> 2);

    private static int Channel(int c, int axis) => (c >> (axis * 5)) & 31;

    /// <summary>The widest of a box's three ranges (0 blue, 1 green, 2 red), and which it is.</summary>
    private static (int Width, int Axis) Range(List<int> box)
    {
        var best = (Width: -1, Axis: 0);
        for (var axis = 0; axis < 3; axis++)
        {
            int lo = 31, hi = 0;
            foreach (var c in box)
            {
                var v = Channel(c, axis);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            if (hi - lo > best.Width) best = (hi - lo, axis);
        }
        return best;
    }
}
