using Compositor.Document;

namespace Compositor.Rendering;

// CPU compositor (upstream LayerRenderer/TiledLayerRenderer for the whole-canvas path):
// walks the layer tree bottom-up, rasterizes each pixel layer through its transform
// (bilinear for Smooth/High, nearest for Nearest), multiplies masks and folder
// opacity/visibility into it, then blends onto the canvas with Photoshop math. GPU
// paths arrive in later milestones; this one is the correctness reference.

public static class LayerRenderer
{
    /// Flattens `document` to a straight RGBA8 buffer (canvas-sized, zeroed = transparent).
    public static byte[] Render(CanvasDocument document)
    {
        var canvas = new byte[document.Width * document.Height * 4];
        foreach (var entry in LayerHierarchy.Entries(document.Layers))
        {
            if (!entry.Visible || entry.Layer.IsGroup || entry.Layer.Pixels is null) continue;
            var layer = entry.Layer;
            double opacity = layer.Opacity;
            // Folder opacity (v8+) dims everything inside it.
            foreach (var ancestor in Ancestors(document, layer.ParentID))
                opacity *= ancestor.Opacity;
            if (opacity <= 0) continue;
            DrawLayer(canvas, document.Width, document.Height, layer, opacity);
        }
        return canvas;
    }

    private static IEnumerable<ImageLayer> Ancestors(CanvasDocument document, Guid? parent)
    {
        while (parent is { } id)
        {
            var group = document.Layers.FirstOrDefault(l => l.Id == id);
            if (group is null) yield break;
            yield return group;
            parent = group.ParentID;
        }
    }

    private static void DrawLayer(byte[] canvas, int canvasWidth, int canvasHeight, ImageLayer layer, double opacity)
    {
        var transform = layer.Transform;
        var rotation = transform.Radians;
        var cos = Math.Cos(-rotation);
        var sin = Math.Sin(-rotation);
        var centerX = transform.Origin.X + transform.Size.Width / 2;
        var centerY = transform.Origin.Y + transform.Size.Height / 2;
        var scaleX = transform.Size.Width / layer.Width;
        var scaleY = transform.Size.Height / layer.Height;

        // The layer's bounding box on the document (rotation-aware).
        var halfW = transform.Size.Width / 2;
        var halfH = transform.Size.Height / 2;
        var radius = Math.Sqrt(halfW * halfW + halfH * halfH);
        var minX = Math.Max(0, (int)Math.Floor(centerX - radius));
        var maxX = Math.Min(canvasWidth - 1, (int)Math.Ceiling(centerX + radius));
        var minY = Math.Max(0, (int)Math.Floor(centerY - radius));
        var maxY = Math.Min(canvasHeight - 1, (int)Math.Ceiling(centerY + radius));

        var mask = layer.Mask;
        var maskEnabled = mask is { Enabled: true };
        // Unlinked masks keep their own placement; linked ones ride the layer transform.
        var maskTransform = layer.Transform;   // mask placement fields arrive with M1.5 editing
        var maskScaleX = mask is null ? 1 : maskTransform.Size.Width / mask.Width;
        var maskScaleY = mask is null ? 1 : maskTransform.Size.Height / mask.Height;

        var nearest = transform.Sampling == LayerSampling.Nearest;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                // Document point → unrotated layer-local pixel coordinates.
                var dx = x + 0.5 - centerX;
                var dy = y + 0.5 - centerY;
                var localX = (dx * cos - dy * sin) / scaleX + layer.Width / 2.0;
                var localY = (dx * sin + dy * cos) / scaleY + layer.Height / 2.0;
                if (localX < 0 || localY < 0 || localX >= layer.Width || localY >= layer.Height) continue;

                // Flips mirror the sample point inside the layer.
                var sampleX = transform.FlipX ? layer.Width - localX : localX;
                var sampleY = transform.FlipY ? layer.Height - localY : localY;

                byte sr, sg, sb, sa;
                var pixels = layer.Pixels!;
                if (nearest)
                {
                    var px = Math.Min(layer.Width - 1, (int)sampleX);
                    var py = Math.Min(layer.Height - 1, (int)sampleY);
                    var at = (py * layer.Width + px) * 4;
                    sr = pixels[at]; sg = pixels[at + 1]; sb = pixels[at + 2]; sa = pixels[at + 3];
                }
                else
                {
                    (sr, sg, sb, sa) = Bilinear(pixels, layer.Width, layer.Height, sampleX, sampleY);
                }

                var coverage = opacity;
                if (maskEnabled)
                {
                    // Same inverse mapping for the mask's own box (linked: identical placement).
                    var mx = sampleX * maskScaleX;
                    var my = sampleY * maskScaleY;
                    var mpx = Math.Clamp((int)mx, 0, mask!.Width - 1);
                    var mpy = Math.Clamp((int)my, 0, mask.Height - 1);
                    coverage *= mask.Gray[mpy * mask.Width + mpx] / 255.0;
                }
                if (coverage <= 0) continue;
                var alpha = sa / 255.0 * coverage;
                if (alpha <= 0) continue;

                var at2 = (y * canvasWidth + x) * 4;
                if (layer.BlendMode == LayerBlendMode.Normal && alpha >= 1)
                {
                    canvas[at2] = sr; canvas[at2 + 1] = sg; canvas[at2 + 2] = sb; canvas[at2 + 3] = 255;
                    continue;
                }

                var backdropA = canvas[at2 + 3] / 255.0;
                if (BlendMath.IsNonSeparable(layer.BlendMode))
                {
                    var blended = BlendMath.NonSeparable(layer.BlendMode,
                        (canvas[at2] / 255.0, canvas[at2 + 1] / 255.0, canvas[at2 + 2] / 255.0),
                        (sr / 255.0, sg / 255.0, sb / 255.0));
                    Composite(canvas, at2, blended.R, blended.G, blended.B, alpha, backdropA);
                }
                else
                {
                    var blendedR = BlendMath.Channel(layer.BlendMode, canvas[at2] / 255.0, sr / 255.0);
                    var blendedG = BlendMath.Channel(layer.BlendMode, canvas[at2 + 1] / 255.0, sg / 255.0);
                    var blendedB = BlendMath.Channel(layer.BlendMode, canvas[at2 + 2] / 255.0, sb / 255.0);
                    Composite(canvas, at2, blendedR, blendedG, blendedB, alpha, backdropA);
                }
            }
        }
    }

    /// Blended color over the backdrop with the layer's alpha (Photoshop: blend happens
    /// in straight color, then plain source-over composites it).
    private static void Composite(byte[] canvas, int at, double r, double g, double b, double alpha, double backdropA)
    {
        var outA = alpha + backdropA * (1 - alpha);
        if (outA <= 0)
        {
            canvas[at] = canvas[at + 1] = canvas[at + 2] = canvas[at + 3] = 0;
            return;
        }
        canvas[at] = ToByte((r * alpha + canvas[at] / 255.0 * backdropA * (1 - alpha)) / outA);
        canvas[at + 1] = ToByte((g * alpha + canvas[at + 1] / 255.0 * backdropA * (1 - alpha)) / outA);
        canvas[at + 2] = ToByte((b * alpha + canvas[at + 2] / 255.0 * backdropA * (1 - alpha)) / outA);
        canvas[at + 3] = ToByte(outA);
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static (byte R, byte G, byte B, byte A) Bilinear(byte[] pixels, int width, int height, double x, double y)
    {
        var x0 = Math.Min((int)x, width - 1);
        var y0 = Math.Min((int)y, height - 1);
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var fx = x - x0;
        var fy = y - y0;
        // Interpolate premultiplied (straight-alpha lerp bleeds color from transparent
        // neighbors), then un-premultiply for the straight pipeline.
        double Lerp(int channel)
        {
            var premultiplied = channel < 3;   // rgb need the source alpha factored in
            double Sample(int at)
            {
                var a = pixels[at + 3] / 255.0;
                return channel < 3 ? pixels[at + channel] * a : pixels[at + 3];
            }
            var top = Sample((y0 * width + x0) * 4) * (1 - fx) + Sample((y0 * width + x1) * 4) * fx;
            var bottom = Sample((y1 * width + x0) * 4) * (1 - fx) + Sample((y1 * width + x1) * 4) * fx;
            return top * (1 - fy) + bottom * fy;
        }
        var a = Lerp(3) / 255.0;
        if (a <= 0) return (0, 0, 0, 0);
        return ((byte)Math.Round(Lerp(0) / a), (byte)Math.Round(Lerp(1) / a),
                (byte)Math.Round(Lerp(2) / a), (byte)Math.Round(Lerp(3)));
    }
}
