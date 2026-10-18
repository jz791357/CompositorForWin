using Compositor.Document;
namespace Compositor.Rendering;

// Photoshop's blend-mode math, straight (un-premultiplied) sRGB in [0,1], from the W3C
// compositing spec as Photoshop implements it. Upstream leans on CoreGraphics for the
// modes it can draw and Core Image for the rest (Color Burn/Dodge, Soft Light, Linear
// Burn/Dodge, Vivid/Linear/Pin Light, Hard Mix, Subtract, Divide); a CPU compositor
// needs every formula in one place. Non-separable modes (Hue…Luminosity) follow the
// spec's lum/sat procedure.

public static class BlendMath
{
    public static double Channel(LayerBlendMode mode, double backdrop, double source) => mode switch
    {
        LayerBlendMode.Normal => source,
        LayerBlendMode.Darken => Math.Min(backdrop, source),
        LayerBlendMode.Multiply => backdrop * source,
        LayerBlendMode.ColorBurn => backdrop >= 1 ? 1 : source <= 0 ? 0 : 1 - (1 - backdrop) / source,
        LayerBlendMode.LinearBurn => Math.Max(0, backdrop + source - 1),
        LayerBlendMode.Lighten => Math.Max(backdrop, source),
        LayerBlendMode.Screen => backdrop + source - backdrop * source,
        LayerBlendMode.ColorDodge => backdrop <= 0 ? 0 : source >= 1 ? 1 : Math.Min(1, backdrop / (1 - source)),
        LayerBlendMode.LinearDodge => Math.Min(1, backdrop + source),
        LayerBlendMode.Overlay => source <= 0.5 ? 2 * backdrop * source : 1 - 2 * (1 - backdrop) * (1 - source),
        LayerBlendMode.SoftLight => SoftLight(backdrop, source),
        LayerBlendMode.HardLight => source <= 0.5 ? 2 * backdrop * source : 1 - 2 * (1 - backdrop) * (1 - source),
        LayerBlendMode.VividLight => source <= 0.5
            ? (backdrop >= 1 ? 1 : 2 * source <= 0 ? 0 : Math.Max(0, 1 - (1 - backdrop) / (2 * source)))
            : (backdrop <= 0 ? 0 : 2 * source - 1 >= 1 ? 1 : Math.Min(1, backdrop / (1 - (2 * source - 1)))),
        LayerBlendMode.LinearLight => Math.Clamp(source <= 0.5
            ? backdrop + 2 * source - 1
            : backdrop + 2 * (source - 0.5), 0, 1),
        LayerBlendMode.PinLight => source <= 0.5 ? Math.Min(backdrop, 2 * source) : Math.Max(backdrop, 2 * source - 1),
        LayerBlendMode.HardMix => Channel(LayerBlendMode.VividLight, backdrop, source) >= 0.5 ? 1 : 0,
        LayerBlendMode.Difference => Math.Abs(backdrop - source),
        LayerBlendMode.Exclusion => backdrop + source - 2 * backdrop * source,
        LayerBlendMode.Subtract => Math.Max(0, backdrop - source),
        LayerBlendMode.Divide => source <= 0 ? 1 : Math.Min(1, backdrop / source),
        // Non-separable: replaced below by the whole-pixel path.
        LayerBlendMode.Hue or LayerBlendMode.Saturation or LayerBlendMode.Color or LayerBlendMode.Luminosity
            => throw new NotSupportedException($"{mode} blends whole pixels"),
        _ => source,
    };

    /// W3C soft light (the Pegtop/Photoshop reading Core Image also follows).
    private static double SoftLight(double b, double s) =>
        s <= 0.5 ? b - (1 - 2 * s) * b * (1 - b)
                 : b + (2 * s - 1) * (D(b) - b);

    private static double D(double x) =>
        x <= 0.25 ? ((16 * x - 12) * x + 4) * x : Math.Sqrt(x);

    public static bool IsNonSeparable(LayerBlendMode mode) =>
        mode is LayerBlendMode.Hue or LayerBlendMode.Saturation
            or LayerBlendMode.Color or LayerBlendMode.Luminosity;

    /// Whole-pixel blend for the non-separable modes, straight sRGB triplets in [0,1].
    public static (double R, double G, double B) NonSeparable(LayerBlendMode mode,
        (double R, double G, double B) backdrop, (double R, double G, double B) source)
    {
        var (br, bg, bb) = backdrop;
        var (sr, sg, sb) = source;
        double Lum((double R, double G, double B) c) => 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
        (double R, double G, double B) ClipColor((double R, double G, double B) c)
        {
            var l = Lum(c);
            var n = Math.Min(Math.Min(c.R, c.G), c.B);
            var x = Math.Max(Math.Max(c.R, c.G), c.B);
            if (n < 0) c = (l + (c.R - l) * l / (l - n), l + (c.G - l) * l / (l - n), l + (c.B - l) * l / (l - n));
            if (x > 1) c = (l + (c.R - l) * (1 - l) / (x - l), l + (c.G - l) * (1 - l) / (x - l), l + (c.B - l) * (1 - l) / (x - l));
            return (Math.Clamp(c.R, 0, 1), Math.Clamp(c.G, 0, 1), Math.Clamp(c.B, 0, 1));
        }
        (double R, double G, double B) SetLum((double R, double G, double B) c, double l)
        {
            var d = l - Lum(c);
            return ClipColor((c.R + d, c.G + d, c.B + d));
        }
        double Sat((double R, double G, double B) c) => Math.Max(Math.Max(c.R, c.G), c.B) - Math.Min(Math.Min(c.R, c.G), c.B);
        (double R, double G, double B) SetSat((double R, double G, double B) c, double s)
        {
            // W3C: keep the low/mid/high channel order, set low to 0, high to s, and
            // scale the mid channel by its share of the original range.
            var channels = new[] { c.R, c.G, c.B };
            var order = channels
                .Select((value, index) => (value, index))
                .OrderBy(p => p.value)
                .ToArray();
            var (min, low) = order[0];
            var (mid, middle) = order[1];
            var (max, high) = order[2];
            if (max <= min) return (0, 0, 0);
            var result = new double[3];
            result[low] = 0;
            result[high] = s;
            result[middle] = s * (mid - min) / (max - min);
            return (result[0], result[1], result[2]);
        }

        switch (mode)
        {
            case LayerBlendMode.Hue:
                return SetLum(SetSat(source, Sat(backdrop)), Lum(backdrop));
            case LayerBlendMode.Saturation:
                return SetLum(SetSat(backdrop, Sat(source)), Lum(backdrop));
            case LayerBlendMode.Color:
                return SetLum(source, Lum(backdrop));
            case LayerBlendMode.Luminosity:
                return SetLum(backdrop, Lum(source));
            default:
                throw new NotSupportedException($"{mode} is separable");
        }
    }
}
