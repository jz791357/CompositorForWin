using Compositor.Document;

namespace Compositor.Rendering;

// Brush stamping into straight-alpha buffers (upstream BrushStroke.swift's engine,
// M1 shape): stamps spaced a quarter of the diameter along the drag, each a radial
// falloff from `hardness` to full opacity, composited straight. Erase lowers alpha;
// mask painting lerps gray coverage.

public static class BrushEngine
{
    public readonly record struct BrushSettings(
        double Size = 24, double Hardness = 0.8, double Opacity = 1.0,
        byte Red = 0, byte Green = 0, byte Blue = 0, bool Erase = false);

    /// Paints the segment `from`→`to` (pixel coordinates in the buffer's space).
    public static void PaintSegment(byte[] pixels, int width, int height,
        CGPoint from, CGPoint to, in BrushSettings brush)
    {
        var spacing = Math.Max(1.0, brush.Size / 4.0);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var stamps = Math.Max(1, (int)Math.Ceiling(length / spacing));
        for (var i = 1; i <= stamps; i++)
        {
            var t = (double)i / stamps;
            Stamp(pixels, width, height, new CGPoint(from.X + dx * t, from.Y + dy * t), brush);
        }
    }

    public static void Stamp(byte[] pixels, int width, int height, CGPoint center, in BrushSettings brush)
    {
        var radius = brush.Size / 2.0;
        if (radius < 0.5)
        {
            var px = (int)Math.Clamp(center.X, 0, width - 1);
            var py = (int)Math.Clamp(center.Y, 0, height - 1);
            Composite(pixels, (py * width + px) * 4, brush.Opacity, brush);
            return;
        }
        var minX = Math.Max(0, (int)Math.Floor(center.X - radius));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(center.X + radius));
        var minY = Math.Max(0, (int)Math.Floor(center.Y - radius));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(center.Y + radius));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var distance = Math.Sqrt((x + 0.5 - center.X) * (x + 0.5 - center.X)
                                       + (y + 0.5 - center.Y) * (y + 0.5 - center.Y));
                if (distance > radius) continue;
                // Hard core, linear falloff to the edge past `hardness`.
                var falloff = brush.Hardness >= 1 ? 0
                    : Math.Clamp((distance / radius - brush.Hardness) / (1 - brush.Hardness), 0, 1);
                var coverage = brush.Opacity * (1 - falloff);
                if (coverage <= 0) continue;
                Composite(pixels, (y * width + x) * 4, coverage, brush);
            }
        }
    }

    private static void Composite(byte[] pixels, int at, double coverage, in BrushSettings brush)
    {
        if (brush.Erase)
        {
            var alpha = pixels[at + 3] / 255.0;
            var updated = alpha * (1 - coverage);
            if (updated <= 0)
            {
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 0;
                return;
            }
            var scale = updated / alpha;   // premultiplied color follows its alpha down
            pixels[at] = (byte)Math.Round(pixels[at] * scale);
            pixels[at + 1] = (byte)Math.Round(pixels[at + 1] * scale);
            pixels[at + 2] = (byte)Math.Round(pixels[at + 2] * scale);
            pixels[at + 3] = (byte)Math.Round(updated * 255);
            return;
        }
        var destinationA = pixels[at + 3] / 255.0;
        var outA = coverage + destinationA * (1 - coverage);
        if (outA <= 0) return;
        // Straight compositing: the brush color is opaque, the backdrop shows through.
        pixels[at] = (byte)Math.Round((brush.Red * coverage + pixels[at] * destinationA * (1 - coverage)) / outA);
        pixels[at + 1] = (byte)Math.Round((brush.Green * coverage + pixels[at + 1] * destinationA * (1 - coverage)) / outA);
        pixels[at + 2] = (byte)Math.Round((brush.Blue * coverage + pixels[at + 2] * destinationA * (1 - coverage)) / outA);
        pixels[at + 3] = (byte)Math.Round(outA * 255);
    }

    /// Paints into a gray mask: white reveals, black hides (`white` picks the value).
    public static void PaintMaskSegment(byte[] gray, int width, int height,
        CGPoint from, CGPoint to, in BrushSettings brush, bool white)
    {
        var spacing = Math.Max(1.0, brush.Size / 4.0);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var stamps = Math.Max(1, (int)Math.Ceiling(length / spacing));
        for (var i = 1; i <= stamps; i++)
        {
            var t = (double)i / stamps;
            StampMask(gray, width, height, new CGPoint(from.X + dx * t, from.Y + dy * t), brush, white);
        }
    }

    private static void StampMask(byte[] gray, int width, int height, CGPoint center, in BrushSettings brush, bool white)
    {
        var radius = brush.Size / 2.0;
        var target = white ? 255.0 : 0.0;
        var minX = Math.Max(0, (int)Math.Floor(center.X - Math.Max(0.5, radius)));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(center.X + Math.Max(0.5, radius)));
        var minY = Math.Max(0, (int)Math.Floor(center.Y - Math.Max(0.5, radius)));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(center.Y + Math.Max(0.5, radius)));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var distance = Math.Sqrt((x + 0.5 - center.X) * (x + 0.5 - center.X)
                                       + (y + 0.5 - center.Y) * (y + 0.5 - center.Y));
                if (distance > radius) continue;
                var falloff = brush.Hardness >= 1 ? 0
                    : Math.Clamp((distance / radius - brush.Hardness) / (1 - brush.Hardness), 0, 1);
                var coverage = brush.Opacity * (1 - falloff);
                if (coverage <= 0) continue;
                var at = y * width + x;
                gray[at] = (byte)Math.Round(target * coverage + gray[at] * (1 - coverage));
            }
        }
    }
}
