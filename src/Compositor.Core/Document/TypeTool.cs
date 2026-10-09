using System.Text.Json.Serialization;

namespace Compositor.Document;

// Upstream TypeTool.swift (text metadata) and ShapeTool.swift (shape metadata), persisted
// halves. Rendering/inline editing arrive with M2.

public enum TextAlignment
{
    [JsonPropertyName("Left")] Left,
    [JsonPropertyName("Center")] Center,
    [JsonPropertyName("Right")] Right,
}

public readonly record struct LayerTextColorRun(int Location, int Length, double Red, double Green, double Blue);

public readonly record struct LayerTextFontRun(int Location, int Length, string FontName);

public sealed class LayerTextStyle
{
    public string Content { get; init; } = "Text";
    public string FontName { get; init; } = "Helvetica";
    public double FontSize { get; init; } = 72;
    public double Red { get; init; }
    public double Green { get; init; }
    public double Blue { get; init; }
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public double Tracking { get; init; }
    /// Baseline to baseline, in layer pixels, as Photoshop's Leading is. 0 is Auto: 120% of the font size.
    public double Leading { get; init; }
    public double AutoLeading => FontSize * 1.2;
    public double LineHeight => Leading > 0 ? Leading : AutoLeading;
    /// The gap between the text and its box, in layer pixels.
    public const double Padding = 12;
    /// Fixed paragraph bounds in layer pixels. Null supports older point-text layers.
    public CGSize? BoxSize { get; init; }

    public bool BoxIsValid => BoxSize is not { } box
        || (double.IsFinite(box.Width) && double.IsFinite(box.Height)
            && box.Width is >= 16 and <= DocumentLimits.MaxSideExtent
            && box.Height is >= 16 and <= DocumentLimits.MaxSideExtent
            && box.Width * box.Height <= DocumentLimits.MaxSurfaceExtent);

    public LayerTextColorRun[]? ColorRuns { get; init; }
    public LayerTextFontRun[]? FontRuns { get; init; }

    private int Utf16Length => Content.Length;

    private bool ColorRunsAreValid
    {
        get
        {
            if (ColorRuns is not { } runs) return true;
            var end = 0;
            foreach (var run in runs)
            {
                if (run.Location < end || run.Length <= 0
                    || new[] { run.Red, run.Green, run.Blue }.Any(v => !double.IsFinite(v) || v is < 0 or > 1))
                    return false;
                end = run.Location + run.Length;
            }
            return runs.Length > 0 && end <= Utf16Length;
        }
    }

    private bool FontRunsAreValid
    {
        get
        {
            if (FontRuns is not { } runs) return true;
            var end = 0;
            foreach (var run in runs)
            {
                if (run.Location < end || run.Length <= 0
                    || string.IsNullOrEmpty(run.FontName) || run.FontName.Length > 200
                    || run.FontName.Any(c => c is '\n' or '\r'))
                    return false;
                end = run.Location + run.Length;
            }
            return runs.Length > 0 && end <= Utf16Length;
        }
    }

    public bool IsValid =>
        Utf16Length <= 100_000 && BoxIsValid
        && double.IsFinite(FontSize) && FontSize is >= 1 and <= 2000
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Tracking) && Tracking is >= -100 and <= 1000
        && double.IsFinite(Leading) && Leading is >= 0 and <= 5000
        && ColorRunsAreValid && FontRunsAreValid;
}

public enum ShapeKind
{
    [JsonPropertyName("Rectangle")] Rectangle,
    [JsonPropertyName("Ellipse")] Ellipse,
    [JsonPropertyName("Line")] Line,
}

public sealed class LayerShapeStyle
{
    public ShapeKind Kind { get; init; }
    public double Red { get; init; }
    public double Green { get; init; }
    public double Blue { get; init; }
    /// Document pixels, whatever size the shape is scaled to.
    public double CornerRadius { get; init; }
    /// A line's thickness, and its two ends as fractions of the layer's box (0–1).
    public double? LineWidth { get; init; }
    public CGPoint? Start { get; init; }
    public CGPoint? End { get; init; }
}
