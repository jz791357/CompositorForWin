using System.Text.Json.Serialization;

namespace Compositor.Document;

// Upstream Levels.swift / Curves.swift / HueSaturation.swift / ImageAdjustments.swift,
// persisted shapes only. Pixel math (tables, LUTs) lands with the M3 adjustment engine.

public enum LevelsChannel
{
    [JsonPropertyName("RGB")] Rgb,
    [JsonPropertyName("Red")] Red,
    [JsonPropertyName("Green")] Green,
    [JsonPropertyName("Blue")] Blue,
}

public readonly record struct LevelRange
{
    public double Black { get; init; }
    public double Gamma { get; init; } = 1;
    public double White { get; init; } = 255;
    public double OutputBlack { get; init; }
    public double OutputWhite { get; init; } = 255;

    // Explicit parameterless ctor: a record struct's synthesized one would zero the
    // initializers above, and JSON decode relies on these defaults.
    public LevelRange() { }

    // Upstream clamps sequentially (white after black), with finiteness fallbacks.
    public LevelRange Normalized
    {
        get
        {
            double Clamp(double n, double low, double high, double fallback) =>
                double.IsFinite(n) ? Math.Min(high, Math.Max(low, n)) : fallback;
            return this with
            {
                Black = Clamp(Black, 0, 254, 0),
                White = Clamp(White, Math.Clamp(Black, 0, 254) + 1, 255, 255),
                Gamma = Clamp(Gamma, 0.1, 9.99, 1),
                OutputBlack = Clamp(OutputBlack, 0, 255, 0),
                OutputWhite = Clamp(OutputWhite, 0, 255, 255),
            };
        }
    }
}

public sealed class LevelsSettings
{
    public LevelsChannel Channel { get; init; } = LevelsChannel.Rgb;
    public LevelRange[] Ranges { get; init; } = [new(), new(), new(), new()];
    public bool IsIdentity => Ranges.All(r => r.Normalized == new LevelRange());
}

public readonly record struct CurvePoint(double X, double Y);

public sealed class CurvesSettings
{
    public LevelsChannel Channel { get; init; } = LevelsChannel.Rgb;
    public CurvePoint[][] Channels { get; init; } =
        [[new(0, 0), new(255, 255)], [new(0, 0), new(255, 255)], [new(0, 0), new(255, 255)], [new(0, 0), new(255, 255)]];

    public bool IsValid =>
        Channels.Length == 4 && Channels.All(points =>
            points.Length is >= 2 and <= 32
            && points[0].X == 0 && points[^1].X == 255
            && points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X is >= 0 and <= 255 && p.Y is >= 0 and <= 255)
            && points.Zip(points.Skip(1), (a, b) => b.X > a.X).All(ordered => ordered));
}

public enum ColorRange
{
    [JsonPropertyName("Master")] Master,
    [JsonPropertyName("Reds")] Reds,
    [JsonPropertyName("Yellows")] Yellows,
    [JsonPropertyName("Greens")] Greens,
    [JsonPropertyName("Cyans")] Cyans,
    [JsonPropertyName("Blues")] Blues,
    [JsonPropertyName("Magentas")] Magentas,
}

public readonly record struct RangeAdjustment(double Hue = 0, double Saturation = 0, double Lightness = 0);

/// One color band's editable limits in degrees (upstream defaultBand).
public readonly record struct HueBand(double FalloffStart, double RangeStart, double RangeEnd, double FalloffEnd)
{
    public static HueBand Default(ColorRange range) => range switch
    {
        ColorRange.Reds => new(315, 345, 15, 45),
        ColorRange.Yellows => new(15, 45, 75, 105),
        ColorRange.Greens => new(75, 105, 135, 165),
        ColorRange.Cyans => new(135, 165, 195, 225),
        ColorRange.Blues => new(195, 225, 255, 285),
        ColorRange.Magentas => new(255, 285, 315, 345),
        _ => new(0, 0, 360, 360),
    };
}

public sealed class HueSaturationSettings
{
    public ColorRange Range { get; init; } = ColorRange.Master;
    public bool Colorize { get; init; }
    public bool InvertRange { get; init; }
    public Dictionary<ColorRange, RangeAdjustment> Adjustments { get; init; } = [];
    public Dictionary<ColorRange, HueBand> Bands { get; init; } =
        Enum.GetValues<ColorRange>().ToDictionary(r => r, HueBand.Default);

    public double Hue => Adjustments.GetValueOrDefault(Range).Hue;
    public double Saturation => Adjustments.GetValueOrDefault(Range).Saturation;
    public double Lightness => Adjustments.GetValueOrDefault(Range).Lightness;

    // Mirrors Swift's memberwise init used by LayerAdjustment.resolvedHSV for projects
    // saved before range-aware HSV existed.
    public HueSaturationSettings() { }
    public HueSaturationSettings(double hue, double saturation, double lightness, bool colorize = false)
    {
        Range = ColorRange.Master;
        Colorize = colorize;
        Adjustments = new Dictionary<ColorRange, RangeAdjustment>
            { [ColorRange.Master] = new(hue, saturation, lightness) };
    }
}

public readonly record struct AdjustmentColor(double Red, double Green, double Blue)
{
    public bool IsValid => new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1);
}

public sealed class ExposureSettings
{
    public static readonly (double, double) ExposureRange = (-20, 20);
    public static readonly (double, double) OffsetRange = (-0.5, 0.5);
    public static readonly (double, double) GammaRange = (0.1, 10);
    public double Exposure { get; init; }
    public double Offset { get; init; }
    public double Gamma { get; init; } = 1;
    public bool IsValid =>
        Exposure >= ExposureRange.Item1 && Exposure <= ExposureRange.Item2
        && Offset >= OffsetRange.Item1 && Offset <= OffsetRange.Item2
        && Gamma >= GammaRange.Item1 && Gamma <= GammaRange.Item2;
}

public sealed class GradientMapSettings
{
    public AdjustmentColor Shadows { get; init; } = new(0, 0, 0);
    public AdjustmentColor Highlights { get; init; } = new(1, 1, 1);
    public bool Reversed { get; init; }
    public bool IsValid => Shadows.IsValid && Highlights.IsValid;
}

public sealed class BlackWhiteSettings
{
    public static readonly (double, double) Range = (-200, 300);
    public double Reds { get; init; } = 40;
    public double Yellows { get; init; } = 60;
    public double Greens { get; init; } = 40;
    public double Cyans { get; init; } = 60;
    public double Blues { get; init; } = 20;
    public double Magentas { get; init; } = 80;
    public bool Tint { get; init; }
    public double TintHue { get; init; } = 40;
    public double TintSaturation { get; init; } = 20;
    public bool IsValid =>
        new[] { Reds, Yellows, Greens, Cyans, Blues, Magentas }
            .All(v => double.IsFinite(v) && v >= Range.Item1 && v <= Range.Item2)
        && double.IsFinite(TintHue) && TintHue is >= 0 and <= 360
        && double.IsFinite(TintSaturation) && TintSaturation is >= 0 and <= 100;
}

public sealed class ColorBalanceSettings
{
    public static readonly (double, double) Range = (-100, 100);
    public double ShadowCyanRed { get; init; }
    public double ShadowMagentaGreen { get; init; }
    public double ShadowYellowBlue { get; init; }
    public double MidCyanRed { get; init; }
    public double MidMagentaGreen { get; init; }
    public double MidYellowBlue { get; init; }
    public double HighlightCyanRed { get; init; }
    public double HighlightMagentaGreen { get; init; }
    public double HighlightYellowBlue { get; init; }
    public bool PreserveLuminosity { get; init; } = true;
    private double[] All => [ShadowCyanRed, ShadowMagentaGreen, ShadowYellowBlue, MidCyanRed, MidMagentaGreen,
        MidYellowBlue, HighlightCyanRed, HighlightMagentaGreen, HighlightYellowBlue];
    public bool IsValid => All.All(v => double.IsFinite(v) && v >= Range.Item1 && v <= Range.Item2);
}

public sealed class GrainSettings
{
    public static readonly (double, double) AmountRange = (0, 100);
    public static readonly (double, double) SizeRange = (0.1, 50);
    public static readonly (double, double) RoughnessRange = (0, 100);
    public double Amount { get; init; } = 25;
    public double Size { get; init; } = 1.5;
    public double Roughness { get; init; } = 50;
    public uint Seed { get; init; }
    public bool IsValid =>
        Amount >= AmountRange.Item1 && Amount <= AmountRange.Item2
        && Size >= SizeRange.Item1 && Size <= SizeRange.Item2
        && Roughness >= RoughnessRange.Item1 && Roughness <= RoughnessRange.Item2;
}
