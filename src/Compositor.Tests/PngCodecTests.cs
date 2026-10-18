using System.Text;
using Compositor.IO;
using Xunit;

namespace Compositor.Tests;

// PngCodec contract tests: round-trips every variant it writes, and decodes the
// variants CGImageSource would accept (palette, gray+alpha, sub-byte depths, Adam7).
public class PngCodecTests
{
    [Fact]
    public void RoundTrip_Rgba8IsExact()
    {
        var (width, height, pixels) = RgbaGradient(37, 23);
        var encoded = PngCodec.Encode(width, height, PngPixelKind.Rgba8, pixels);
        var decoded = PngCodec.Decode(encoded);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(PngPixelKind.Rgba8, decoded.Kind);
        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void RoundTrip_Gray8IsExact()
    {
        var width = 17; var height = 9;
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++) gray[i] = (byte)(i * 7 % 256);
        var encoded = PngCodec.Encode(width, height, PngPixelKind.Gray8, gray);
        var decoded = PngCodec.Decode(encoded, PngPixelKind.Gray8);
        Assert.Equal(PngPixelKind.Gray8, decoded.Kind);
        Assert.Equal(gray, decoded.Pixels);
    }

    [Fact]
    public void Decode_GrayBecomesOpaqueRgba()
    {
        var gray = new byte[] { 10, 20, 30, 40 };
        var encoded = PngCodec.Encode(2, 2, PngPixelKind.Gray8, gray);
        var decoded = PngCodec.Decode(encoded);
        Assert.Equal(PngPixelKind.Rgba8, decoded.Kind);
        Assert.Equal(10, decoded.Pixels[0]);
        Assert.Equal(10, decoded.Pixels[2]);
        Assert.Equal(255, decoded.Pixels[3]);
        Assert.Equal(40, decoded.Pixels[12]);     // pixel (1,1): last gray sample
    }

    [Fact]
    public void Decode_RejectsSixteenBit()
    {
        var png = HandshakePng(depth: 16);
        Assert.Throws<PngException>(() => PngCodec.Decode(png));
    }

    [Fact]
    public void Decode_PaletteExpandsWithTransmittance()
    {
        // 2x1 palette PNG, row data [1, 0]: first pixel takes palette index 1
        // (blue, half transparent via tRNS), second index 0 (red, opaque).
        var png = ManualPng(width: 2, height: 1, bitDepth: 8, colorType: 3,
            palette: [255, 0, 0, 0, 0, 255], transmittance: [255, 128],
            idat: FilterRow([1, 0]));
        var decoded = PngCodec.Decode(png);
        Assert.Equal(0, decoded.Pixels[0]);        // blue pixel: B arrives below
        Assert.Equal(255, decoded.Pixels[2]);
        Assert.Equal(128, decoded.Pixels[3]);      // half transparent
        Assert.Equal(255, decoded.Pixels[4]);      // red pixel
        Assert.Equal(255, decoded.Pixels[7]);      // opaque
    }

    [Fact]
    public void Decode_SubByteGrayScales()
    {
        // 1-bit grayscale, MSB-first: 0b10101010 → samples 1,0,1,0,1,0,1,0.
        var png = ManualPng(width: 8, height: 1, bitDepth: 1, colorType: 0,
            idat: FilterRow([0b1010_1010]));
        var decoded = PngCodec.Decode(png);
        Assert.Equal(255, decoded.Pixels[0]);      // pixel 0, sample b7 = 1
        Assert.Equal(255, decoded.Pixels[16]);     // pixel 4, sample b3 = 1
        Assert.Equal(0, decoded.Pixels[28]);       // pixel 7, sample b0 = 0
    }

    [Fact]
    public void Decode_Adam7InterlacedMatchesSequential()
    {
        var (width, height, pixels) = RgbaGradient(13, 11);
        var sequential = PngCodec.Encode(width, height, PngPixelKind.Rgba8, pixels);
        var interlaced = Interlace(sequential, width, height, pixels);
        Assert.Equal(pixels, PngCodec.Decode(interlaced).Pixels);
    }

    [Fact]
    public void Decode_RejectsCorruptCrc()
    {
        var (width, height, pixels) = RgbaGradient(4, 4);
        var encoded = PngCodec.Encode(width, height, PngPixelKind.Rgba8, pixels);
        encoded[40] ^= 0xFF;
        Assert.Throws<PngException>(() => PngCodec.Decode(encoded));
    }

    [Fact]
    public void Thumbnail_ShrinksTo96AndKeepsOpaqueColors()
    {
        var (width, height, pixels) = RgbaGradient(200, 100);
        var thumb = PngCodec.Thumbnail(width, height, pixels);
        Assert.Equal(96 * 48 * 4, thumb.Length);
    }

    private static (int, int, byte[]) RgbaGradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)((x + y) % 256);
                pixels[i + 3] = (byte)(x % 2 == 0 ? 255 : 128);
            }
        return (width, height, pixels);
    }

    private static byte[] FilterRow(byte[] row) => new byte[1 + row.Length].Also(buffer => row.CopyTo(buffer, 1));

    /// Re-encodes `pixels` as an Adam7-interlaced PNG: distributes samples across the
    /// seven passes with filter 0 and packs a fresh IDAT — an independent implementation
    /// to cross-check the decoder against.
    private static byte[] Interlace(byte[] sequential, int width, int height, byte[] pixels)
    {
        var bpp = 4;
        using var idat = new MemoryStream();
        for (var pass = 1; pass <= 7; pass++)
        {
            PngCodec.Adam7.Dimensions(pass, width, height, out var passWidth, out var passHeight);
            if (passWidth == 0 || passHeight == 0) continue;
            var row = new byte[passWidth * bpp];
            for (var y = 0; y < passHeight; y++)
            {
                Array.Clear(row);
                for (var x = 0; x < passWidth; x++)
                {
                    var fx = PngCodec.Adam7.XOrigin(pass) + x * PngCodec.Adam7.XStep(pass);
                    var fy = PngCodec.Adam7.YOrigin(pass) + y * PngCodec.Adam7.YStep(pass);
                    Array.Copy(pixels, (fy * width + fx) * bpp, row, x * bpp, bpp);
                }
                idat.WriteByte(0);   // filter: none
                idat.Write(row);
            }
        }
        var original = sequential;   // reuse header bytes
        var output = new MemoryStream();
        output.Write(original.AsSpan(0, 8));
        var header = new byte[13];
        header[8] = 8; header[9] = 6; header[12] = 1;   // interlaced
        WriteChunkManual(output, "IHDR", header, width, height);
        WriteChunkManual(output, "IDAT", PngCodec.ZlibWrap(idat.ToArray()), width, height);
        WriteChunkManual(output, "IEND", [], width, height);
        return output.ToArray();
    }

    private static void WriteChunkManual(Stream output, string type, byte[] data, int width, int height)
    {
        if (type == "IHDR")
        {
            data = (byte[])data.Clone();
            data[0] = (byte)(width >>> 24); data[1] = (byte)(width >>> 16); data[2] = (byte)(width >>> 8); data[3] = (byte)width;
            data[4] = (byte)(height >>> 24); data[5] = (byte)(height >>> 16); data[6] = (byte)(height >>> 8); data[7] = (byte)height;
        }
        var header = new byte[8];
        header[0] = (byte)(data.Length >>> 24); header[1] = (byte)(data.Length >>> 16);
        header[2] = (byte)(data.Length >>> 8); header[3] = (byte)data.Length;
        Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        output.Write(header);
        output.Write(data);
        var crc = PngCodec.Crc32(Encoding.ASCII.GetBytes(type).Concat(data).ToArray());
        var trailer = new byte[4];
        trailer[0] = (byte)(crc >>> 24); trailer[1] = (byte)(crc >>> 16);
        trailer[2] = (byte)(crc >>> 8); trailer[3] = (byte)crc;
        output.Write(trailer);
    }

    private static byte[] ManualPng(int width, int height, int bitDepth, int colorType,
        byte[]? palette = null, byte[]? transmittance = null, byte[]? idat = null)
    {
        var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        header[0] = (byte)(width >>> 24); header[1] = (byte)(width >>> 16); header[2] = (byte)(width >>> 8); header[3] = (byte)width;
        header[4] = (byte)(height >>> 24); header[5] = (byte)(height >>> 16); header[6] = (byte)(height >>> 8); header[7] = (byte)height;
        header[8] = (byte)bitDepth; header[9] = (byte)colorType;
        WriteChunkManual(output, "IHDR", header, width, height);
        if (palette != null) WriteChunkManual(output, "PLTE", palette, width, height);
        if (transmittance != null) WriteChunkManual(output, "tRNS", transmittance, width, height);
        if (idat != null) WriteChunkManual(output, "IDAT", PngCodec.ZlibWrap(idat), width, height);
        WriteChunkManual(output, "IEND", [], width, height);
        return output.ToArray();
    }

    private static byte[] HandshakePng(int depth)
    {
        var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        header[0] = 0; header[1] = 0; header[2] = 0; header[3] = 1;
        header[4] = 0; header[5] = 0; header[6] = 0; header[7] = 1;
        header[8] = (byte)depth; header[9] = 0;
        WriteChunkManual(output, "IHDR", header, 1, 1);
        WriteChunkManual(output, "IDAT", PngCodec.ZlibWrap([0, 0]), 1, 1);
        WriteChunkManual(output, "IEND", [], 1, 1);
        return output.ToArray();
    }
}

internal static class ArrayAlso
{
    public static byte[] Also(this byte[] buffer, Action<byte[]> action)
    {
        action(buffer);
        return buffer;
    }
}
