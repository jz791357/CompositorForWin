using System.IO.Compression;
using System.Text;
using Compositor.Document;

namespace Compositor.IO;

// PNG decode/encode standing in for CoreGraphics/ImageIO (upstream reads assets through
// CGImageSource and writes them through CGImageDestination). Decoding accepts every
// 8-bit-or-less PNG variant those APIs accept — grayscale, palette, gray+alpha, RGB,
// RGBA, Adam7 interlacing — and yields straight-alpha RGBA8, or Gray8 for masks.
// Encoding always writes non-interlaced RGBA8/Gray8 with per-row filter selection.

public enum PngPixelKind { Rgba8, Gray8 }

public sealed record PngImage(int Width, int Height, PngPixelKind Kind, byte[] Pixels);

public sealed class PngException : Exception
{
    public PngException(string message) : base(message) { }
}

public static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    // --- Decode ---

    public static PngImage Decode(byte[] bytes, PngPixelKind kind = PngPixelKind.Rgba8)
    {
        var header = ParseHeader(bytes);
        var samples = UnfilterAndScale(bytes, header, scaleIndices: header.ColorType != 3);
        if (kind == PngPixelKind.Gray8)
        {
            if (header.ColorType != 0 || header.BitDepth != 8)
                throw new PngException("masks must be 8-bit grayscale without alpha");
            return new PngImage(header.Width, header.Height, kind, samples);
        }
        var rgba = ExpandToRgba(samples, header);
        return new PngImage(header.Width, header.Height, PngPixelKind.Rgba8, rgba);
    }

    private readonly record struct Header(int Width, int Height, int BitDepth, int ColorType, int Interlace,
        byte[]? Palette, byte[]? Transmittance, byte[] Compressed, bool SawIend);

    private static Header ParseHeader(byte[] bytes)
    {
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
            throw new PngException("not a PNG");
        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null, transmittance = null;
        var compressed = new MemoryStream();
        var sawIend = false;
        for (var offset = 8; offset + 12 <= bytes.Length && !sawIend;)
        {
            var length = ReadInt32BE(bytes, offset);
            if (length < 0 || offset + 12 + length > bytes.Length) throw new PngException("truncated chunk");
            if (Crc32(bytes.AsSpan(offset + 4, 4 + length)) != (uint)ReadInt32BE(bytes, offset + 8 + length))
                throw new PngException("chunk CRC mismatch");
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            switch (type)
            {
                case "IHDR":
                    if (length != 13) throw new PngException("bad IHDR");
                    width = ReadInt32BE(bytes, offset + 8);
                    height = ReadInt32BE(bytes, offset + 12);
                    bitDepth = bytes[offset + 16];
                    colorType = bytes[offset + 17];
                    interlace = bytes[offset + 20];
                    break;
                case "PLTE":
                    palette = bytes.AsSpan(offset + 8, length).ToArray();
                    break;
                case "tRNS":
                    transmittance = bytes.AsSpan(offset + 8, length).ToArray();
                    break;
                case "IDAT":
                    compressed.Write(bytes.AsSpan(offset + 8, length));
                    break;
                case "IEND":
                    sawIend = true;
                    break;
                case "acTL":
                    throw new PngException("animated PNGs are not single images");
            }
            offset += 12 + length;
        }
        if (width <= 0 || height <= 0 || colorType < 0 || !sawIend || compressed.Length == 0)
            throw new PngException("incomplete PNG");
        if (bitDepth > 8) throw new PngException("only 8-bit-or-less PNGs decode");
        if (width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide)
            throw new PngException("image exceeds the side limit");
        var combinationOk = (colorType, bitDepth) switch
        {
            (0, 1 or 2 or 4 or 8) or (2, 8) or (3, 1 or 2 or 4 or 8) or (4, 8) or (6, 8) => true,
            _ => false,
        };
        if (!combinationOk) throw new PngException("unsupported color type / bit depth combination");
        if (colorType == 3 && (palette == null || palette.Length % 3 != 0 || palette.Length == 0))
            throw new PngException("palette missing");
        return new Header(width, height, bitDepth, colorType, interlace, palette, transmittance,
            compressed.ToArray(), sawIend);
    }

    private static int SamplesPerPixel(int colorType) =>
        colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new PngException("bad color type") };

    /// Unfilters the zlib stream and returns `width * height * samplesPerPixel` scaled
    /// 8-bit samples. `scaleIndices` false keeps palette bytes as indices (the PNG spec
    /// scales samples, not indices).
    private static byte[] UnfilterAndScale(byte[] bytes, Header header, bool scaleIndices)
    {
        var raw = InflateZlib(header.Compressed);
        var samplesPerPixel = SamplesPerPixel(header.ColorType);
        var bpp = Math.Max(1, samplesPerPixel * header.BitDepth / 8);
        var samples = new byte[header.Width * header.Height * samplesPerPixel];

        void ConsumeGrid(int gridWidth, int gridHeight, int input, Func<int, int, (int x, int y)> placement)
        {
            var rowBytes = (gridWidth * samplesPerPixel * header.BitDepth + 7) / 8;
            if (input + gridHeight * (1 + rowBytes) > raw.Length) throw new PngException("pixel data short");
            var canvas = new byte[gridHeight * rowBytes];
            for (var y = 0; y < gridHeight; y++)
                UnfilterRow(raw, input + y * (1 + rowBytes), rowBytes, bpp, canvas, y * rowBytes, rowBytes, y > 0);
            for (var y = 0; y < gridHeight; y++)
                for (var x = 0; x < gridWidth; x++)
                {
                    var (targetX, targetY) = placement(x, y);
                    for (var s = 0; s < samplesPerPixel; s++)
                        samples[(targetY * header.Width + targetX) * samplesPerPixel + s] =
                            ReadSample(canvas, y * rowBytes, x * samplesPerPixel + s, header.BitDepth, scaleIndices);
                }
        }

        if (header.Interlace == 0)
        {
            ConsumeGrid(header.Width, header.Height, 0, (x, y) => (x, y));
            return samples;
        }
        var consumed = 0;
        for (var pass = 1; pass <= 7; pass++)
        {
            Adam7.Dimensions(pass, header.Width, header.Height, out var passWidth, out var passHeight);
            if (passWidth == 0 || passHeight == 0) continue;
            var rowBytes = (passWidth * samplesPerPixel * header.BitDepth + 7) / 8;
            ConsumeGrid(passWidth, passHeight, consumed,
                (x, y) => (Adam7.XOrigin(pass) + x * Adam7.XStep(pass), Adam7.YOrigin(pass) + y * Adam7.YStep(pass)));
            consumed += passHeight * (1 + rowBytes);
        }
        return samples;
    }

    private static void UnfilterRow(byte[] raw, int rowStart, int rowBytes, int bpp,
        byte[] canvas, int canvasOffset, int stride, bool hasPriorRow)
    {
        var filter = raw[rowStart];
        for (var i = 0; i < rowBytes; i++)
        {
            var value = raw[rowStart + 1 + i];
            var left = i >= bpp ? canvas[canvasOffset + i - bpp] : (byte)0;
            var up = hasPriorRow ? canvas[canvasOffset - stride + i] : (byte)0;
            var upperLeft = hasPriorRow && i >= bpp ? canvas[canvasOffset - stride + i - bpp] : (byte)0;
            canvas[canvasOffset + i] = filter switch
            {
                0 => value,
                1 => (byte)(value + left),
                2 => (byte)(value + up),
                3 => (byte)(value + ((left + up) / 2)),
                4 => (byte)(value + Paeth(left, up, upperLeft)),
                _ => throw new PngException("bad filter type"),
            };
        }
    }

    private static byte ReadSample(byte[] row, int rowStart, int sampleIndex, int bitDepth, bool scale)
    {
        if (bitDepth == 8) return row[rowStart + sampleIndex];
        var bit = sampleIndex * bitDepth;
        var packed = row[rowStart + bit / 8];
        var value = bitDepth switch
        {
            1 => (packed >> (7 - bit % 8)) & 1,
            2 => (packed >> (6 - bit % 8)) & 3,
            4 => (packed >> (4 - bit % 8)) & 15,
            _ => throw new PngException("bad bit depth"),
        };
        return scale ? (byte)(value * 255 / ((1 << bitDepth) - 1)) : (byte)value;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] ExpandToRgba(byte[] samples, Header header)
    {
        var count = header.Width * header.Height;
        var rgba = new byte[count * 4];
        switch (header.ColorType)
        {
            case 0:
                for (var i = 0; i < count; i++)
                {
                    rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = samples[i];
                    rgba[i * 4 + 3] = 255;
                }
                break;
            case 2:
                for (var i = 0; i < count; i++)
                {
                    rgba[i * 4] = samples[i * 3];
                    rgba[i * 4 + 1] = samples[i * 3 + 1];
                    rgba[i * 4 + 2] = samples[i * 3 + 2];
                    rgba[i * 4 + 3] = 255;
                }
                break;
            case 3:
                var palette = header.Palette!;
                for (var i = 0; i < count; i++)
                {
                    var index = samples[i];
                    if (index * 3 + 2 >= palette.Length) throw new PngException("palette index out of range");
                    rgba[i * 4] = palette[index * 3];
                    rgba[i * 4 + 1] = palette[index * 3 + 1];
                    rgba[i * 4 + 2] = palette[index * 3 + 2];
                    rgba[i * 4 + 3] = index < (header.Transmittance?.Length ?? 0) ? header.Transmittance![index] : (byte)255;
                }
                break;
            case 4:
                for (var i = 0; i < count; i++)
                {
                    rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = samples[i * 2];
                    rgba[i * 4 + 3] = samples[i * 2 + 1];
                }
                break;
            case 6:
                Array.Copy(samples, rgba, count * 4);
                break;
        }
        return rgba;
    }

    // --- Encode (straight RGBA8 or Gray8 in, non-interlaced PNG out) ---

    public static byte[] Encode(int width, int height, PngPixelKind kind, byte[] pixels)
    {
        var channels = kind == PngPixelKind.Gray8 ? 1 : 4;
        var rowBytes = width * channels;
        if (pixels.Length < rowBytes * height) throw new PngException("pixel buffer short");

        var filtered = new byte[height * (1 + rowBytes)];
        var priorRow = new byte[rowBytes];   // raw pixel bytes of the previous row
        var currentRow = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * rowBytes, currentRow, 0, rowBytes);
            var bestFilter = 0;
            byte[] bestBytes = [];
            long bestScore = long.MaxValue;
            for (var filter = 0; filter <= 4; filter++)
            {
                var candidate = new byte[rowBytes];
                long score = 0;
                for (var i = 0; i < rowBytes; i++)
                {
                    var left = i >= channels ? currentRow[i - channels] : (byte)0;
                    var up = priorRow[i];
                    var upperLeft = i >= channels ? priorRow[i - channels] : (byte)0;
                    var predicted = filter switch
                    {
                        0 => (byte)0,
                        1 => left,
                        2 => up,
                        3 => (byte)((left + up) / 2),
                        _ => Paeth(left, up, upperLeft),
                    };
                    candidate[i] = (byte)(currentRow[i] - predicted);
                    score += Math.Abs((int)(sbyte)candidate[i]);
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFilter = filter;
                    bestBytes = candidate;
                }
            }
            filtered[y * (1 + rowBytes)] = (byte)bestFilter;
            bestBytes.CopyTo(filtered, y * (1 + rowBytes) + 1);
            (priorRow, currentRow) = (currentRow, priorRow);   // this row becomes the prior
        }

        var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        WriteInt32BE(header, 0, width); WriteInt32BE(header, 4, height);
        header[8] = 8;
        header[9] = kind == PngPixelKind.Gray8 ? (byte)0 : (byte)6;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", ZlibWrap(filtered));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// A ≤96-pixel thumbnail (upstream's kCGImageSourceThumbnailMaxPixelSize: 96),
    /// area-averaged with premultiplied alpha so transparent edges stay clean.
    public static byte[] Thumbnail(int width, int height, byte[] rgba)
    {
        var scale = Math.Max(width, height) <= 96 ? 1 : 96.0 / Math.Max(width, height);
        var thumbWidth = Math.Max(1, (int)Math.Round(width * scale));
        var thumbHeight = Math.Max(1, (int)Math.Round(height * scale));
        var result = new byte[thumbWidth * thumbHeight * 4];
        for (var ty = 0; ty < thumbHeight; ty++)
        {
            for (var tx = 0; tx < thumbWidth; tx++)
            {
                var x0 = tx * (double)width / thumbWidth; var x1 = (tx + 1) * (double)width / thumbWidth;
                var y0 = ty * (double)height / thumbHeight; var y1 = (ty + 1) * (double)height / thumbHeight;
                double r = 0, g = 0, b = 0, a = 0, area = 0;
                for (var sy = (int)Math.Floor(y0); sy < (int)Math.Ceiling(y1) && sy < height; sy++)
                    for (var sx = (int)Math.Floor(x0); sx < (int)Math.Ceiling(x1) && sx < width; sx++)
                    {
                        var weight = (Math.Min(x1, sx + 1) - Math.Max(x0, sx)) * (Math.Min(y1, sy + 1) - Math.Max(y0, sy));
                        var index = (sy * width + sx) * 4;
                        var alpha = rgba[index + 3] / 255.0;
                        r += rgba[index] * alpha * weight;
                        g += rgba[index + 1] * alpha * weight;
                        b += rgba[index + 2] * alpha * weight;
                        a += rgba[index + 3] * weight;
                        area += weight;
                    }
                var overall = a / area / 255.0;
                var target = (ty * thumbWidth + tx) * 4;
                result[target] = overall > 0 ? (byte)Math.Round(r / area / overall) : (byte)0;
                result[target + 1] = overall > 0 ? (byte)Math.Round(g / area / overall) : (byte)0;
                result[target + 2] = overall > 0 ? (byte)Math.Round(b / area / overall) : (byte)0;
                result[target + 3] = (byte)Math.Round(a / area);
            }
        }
        return result;
    }

    // --- Adam7 pass geometry (RFC 2083: the 8x8 matrix) ---

    internal static class Adam7
    {
        private static readonly (int XOrigin, int XStep, int YOrigin, int YStep)[] Passes =
        [
            (0, 8, 0, 8), (4, 8, 0, 8), (0, 4, 4, 8),
            (2, 4, 0, 4), (0, 2, 2, 4),
            (1, 2, 0, 2),
            (0, 1, 1, 2),   // the last pass covers whole odd rows
        ];

        public static void Dimensions(int pass, int width, int height, out int passWidth, out int passHeight)
        {
            var (xOrigin, xStep, yOrigin, yStep) = Passes[pass - 1];
            passWidth = width > xOrigin ? (width - xOrigin + xStep - 1) / xStep : 0;
            passHeight = height > yOrigin ? (height - yOrigin + yStep - 1) / yStep : 0;
        }

        public static int XOrigin(int pass) => Passes[pass - 1].XOrigin;
        public static int YOrigin(int pass) => Passes[pass - 1].YOrigin;
        public static int XStep(int pass) => Passes[pass - 1].XStep;
        public static int YStep(int pass) => Passes[pass - 1].YStep;
    }

    // --- checksums & zlib ---

    internal static byte[] InflateZlib(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, 2, compressed.Length - 2 - 4); // skip zlib header + adler32
        using var inflater = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        return output.ToArray();
    }

    internal static byte[] ZlibWrap(byte[] data)
    {
        using var deflated = new MemoryStream();
        using (var deflate = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);
        using var wrapped = new MemoryStream();
        wrapped.Write([0x78, 0x01]);
        wrapped.Write(deflated.ToArray());
        var adler = BitConverter.GetBytes(Adler32(data));
        if (BitConverter.IsLittleEndian) Array.Reverse(adler);
        wrapped.Write(adler);
        return wrapped.ToArray();
    }

    internal static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (crc & 1));
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var header = new byte[8];
        WriteInt32BE(header, 0, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        output.Write(header);
        output.Write(data);
        var crc = Crc32(Encoding.ASCII.GetBytes(type).Concat(data).ToArray());
        var trailer = new byte[4];
        WriteInt32BE(trailer, 0, unchecked((int)crc));
        output.Write(trailer);
    }

    internal static int ReadInt32BE(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static void WriteInt32BE(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >>> 24);
        target[offset + 1] = (byte)(value >>> 16);
        target[offset + 2] = (byte)(value >>> 8);
        target[offset + 3] = (byte)value;
    }
}
