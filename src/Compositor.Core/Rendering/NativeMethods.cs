using System.Runtime.InteropServices;

namespace Compositor.Rendering;

// Bindings for the upstream C pixel kernels (src/Compositor.Native/c — compiled
// verbatim from the macOS project). Signatures mirror the .h contracts there;
// pixels are premultiplied RGBA unless a header says otherwise.
internal static partial class NativeMethods
{
    private const string Library = "Compositor.Native";

    // BrushPixels.h — half-open bounds of nonzero alpha; empty returns all zero.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void brush_alpha_bounds(byte* bytes, nuint width, nuint height, nuint stride, nuint* bounds);

    // NoisePixels.h — position- and seed-stable grain; alpha untouched.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void noise_add(byte* rgba, nuint width, nuint height, nuint stride,
        float amount, int gaussian, int monochromatic, uint seed);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe void noise_add_at(byte* rgba, nuint width, nuint height, nuint stride,
        float amount, int gaussian, int monochromatic, uint seed, long originX, long originY);

    // WandPixels.h — magic wand fill; returns pixels selected or -1 on allocation failure.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe long wand_mask(byte* rgba, nuint width, nuint height, nuint stride,
        nuint seedX, nuint seedY, nuint radius, int tolerance, int contiguous, byte* mask);

    // WandPixels.h — outline loops of a mask; points/loops are malloc'd (freed upstream
    // with free(); callers here own them until a NativeFree binding lands with M1).
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int wand_trace(byte* mask, nuint width, nuint height,
        int** points, nuint* pointCount, int** loops, nuint* loopCount);
}
