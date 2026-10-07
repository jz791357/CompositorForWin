using Compositor.Rendering;
using Xunit;

namespace Compositor.Tests;

// Smoke tests for the C kernel DLL: each asserts only what the upstream headers
// contractually promise. The full behavioral suite ports from CompositorTests
// alongside each milestone's features.
public class NativeKernelTests
{
    [Fact]
    unsafe void BrushAlphaBounds_AllTransparent_ReturnsAllZeroBounds()
    {
        var pixels = new byte[4 * 4 * 4]; // 4x4 fully transparent
        var bounds = stackalloc nuint[4];
        fixed (byte* p = pixels)
        {
            NativeMethods.brush_alpha_bounds(p, 4, 4, 16, bounds);
        }
        Assert.Equal((nuint)0, bounds[0]);
        Assert.Equal((nuint)0, bounds[1]);
        Assert.Equal((nuint)0, bounds[2]);
        Assert.Equal((nuint)0, bounds[3]);
    }

    [Fact]
    unsafe void BrushAlphaBounds_OpaquePixel_BoundsAreNotAllZero()
    {
        var pixels = new byte[4 * 4 * 4];
        pixels[(1 * 4 + 2) * 4 + 3] = 255; // opaque pixel at (2, 1)
        var bounds = stackalloc nuint[4];
        fixed (byte* p = pixels)
        {
            NativeMethods.brush_alpha_bounds(p, 4, 4, 16, bounds);
        }
        Assert.True(bounds[0] != 0 || bounds[1] != 0 || bounds[2] != 0 || bounds[3] != 0);
    }

    [Fact]
    unsafe void NoiseAdd_SameSeed_ProducesIdenticalGrain()
    {
        var a = SolidImage(16, 16, 100, 120, 140, 255);
        var b = SolidImage(16, 16, 100, 120, 140, 255);
        fixed (byte* pa = a, pb = b)
        {
            NativeMethods.noise_add(pa, 16, 16, 64, 25.0f, gaussian: 0, monochromatic: 0, seed: 7);
            NativeMethods.noise_add(pb, 16, 16, 64, 25.0f, gaussian: 0, monochromatic: 0, seed: 7);
        }
        Assert.Equal(a, b);
    }

    [Fact]
    unsafe void NoiseAdd_DifferentSeed_ProducesDifferentGrain_LeavesAlphaIntact()
    {
        var a = SolidImage(16, 16, 100, 120, 140, 200);
        var b = SolidImage(16, 16, 100, 120, 140, 200);
        fixed (byte* pa = a, pb = b)
        {
            NativeMethods.noise_add(pa, 16, 16, 64, 25.0f, gaussian: 0, monochromatic: 0, seed: 7);
            NativeMethods.noise_add(pb, 16, 16, 64, 25.0f, gaussian: 0, monochromatic: 0, seed: 8);
        }
        Assert.NotEqual(a, b);
        for (int i = 3; i < a.Length; i += 4)
            Assert.Equal(200, a[i]);
    }

    [Fact]
    unsafe void WandMask_SolidContiguousImage_SelectsEveryPixel()
    {
        var rgba = SolidImage(4, 4, 90, 90, 90, 255);
        var mask = new byte[16];
        long selected;
        fixed (byte* p = rgba, m = mask)
        {
            selected = NativeMethods.wand_mask(p, 4, 4, 16, seedX: 1, seedY: 1, radius: 1,
                tolerance: 0, contiguous: 1, mask: m);
        }
        Assert.Equal(16, selected);
        Assert.All(mask, value => Assert.Equal(255, value));
    }

    [Fact]
    unsafe void WandTrace_FullSelection_OutlinesOneLoop()
    {
        var mask = new byte[16];
        Array.Fill(mask, (byte)255);
        int* points = null, loops = null;
        nuint pointCount = 0, loopCount = 0;
        int result;
        fixed (byte* m = mask)
        {
            result = NativeMethods.wand_trace(m, 4, 4, &points, &pointCount, &loops, &loopCount);
        }
        // The outline buffers are kernel mallocs; deliberately leaked here (process exit
        // reclaims them) until the Native free binding lands with M1's renderer.
        Assert.Equal(0, result);
        Assert.Equal((nuint)1, loopCount);
        Assert.True(pointCount >= 4);
    }

    private static byte[] SolidImage(int width, int height, byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = a;
        }
        return pixels;
    }
}
