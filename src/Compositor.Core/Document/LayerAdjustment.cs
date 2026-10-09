using System.Text.Json.Serialization;

namespace Compositor.Document;

// Upstream LayerAdjustment.swift, model half: which adjustment a layer carries and the
// settings of every kind, all optional-and-defaulting so old files decode unchanged.
// The pixel `apply` engine arrives with M3.

public enum AdjustmentKind
{
    [JsonPropertyName("Hue/Saturation")] Hsv,
    [JsonPropertyName("Levels")] Levels,
    [JsonPropertyName("Curves")] Curves,
    [JsonPropertyName("Exposure")] Exposure,
    [JsonPropertyName("Gradient Map")] GradientMap,
    [JsonPropertyName("Grain")] Grain,
    [JsonPropertyName("Add Noise")] AddNoise,
    [JsonPropertyName("Gaussian Blur")] GaussianBlur,
    [JsonPropertyName("Motion Blur")] MotionBlur,
    [JsonPropertyName("Invert")] Invert,
    [JsonPropertyName("Black & White")] BlackWhite,
    [JsonPropertyName("Color Balance")] ColorBalance,
}

public sealed class LayerAdjustment
{
    public AdjustmentKind Kind { get; init; }
    public double Hue { get; init; }
    public double Saturation { get; init; }
    public double Lightness { get; init; }
    public bool Colorize { get; init; }
    /// Optional so projects saved before range-aware HSV adjustments still decode.
    public HueSaturationSettings? HsvSettings { get; init; }
    public HueSaturationSettings ResolvedHSV =>
        HsvSettings ?? new HueSaturationSettings(Hue, Saturation, Lightness, Colorize);
    public LevelsSettings Levels { get; init; } = new();
    public CurvesSettings Curves { get; init; } = new();
    // Optional so projects saved before these adjustments existed decode, and save, exactly as before.
    public ExposureSettings? ExposureSettings { get; init; }
    public GradientMapSettings? GradientMapSettings { get; init; }
    public GrainSettings? GrainSettings { get; init; }
    public BlackWhiteSettings? BlackWhiteSettings { get; init; }
    public ColorBalanceSettings? ColorBalanceSettings { get; init; }
    public double? BlurRadius { get; init; }
    public double? MotionAngle { get; init; }
    public double? MotionDistance { get; init; }
    public double? NoiseAmount { get; init; }
    public bool? NoiseGaussian { get; init; }
    public bool? NoiseMonochromatic { get; init; }
    public uint? NoiseSeed { get; init; }

    public double GaussianRadius => BlurRadius ?? 10;
    public double ResolvedMotionAngle => MotionAngle ?? 0;
    public double ResolvedMotionDistance => MotionDistance ?? 1;
    public double ResolvedNoiseAmount => NoiseAmount ?? 25;

    public bool IsValid
    {
        get
        {
            var hsv = ResolvedHSV;
            if (!double.IsFinite(Hue) || !double.IsFinite(Saturation) || !double.IsFinite(Lightness)
                || Math.Abs(Hue) > 360 || Math.Abs(Saturation) > 100 || Math.Abs(Lightness) > 100)
                return false;
            foreach (var adjustment in hsv.Adjustments.Values)
                if (!double.IsFinite(adjustment.Hue) || Math.Abs(adjustment.Hue) > 360
                    || !double.IsFinite(adjustment.Saturation) || Math.Abs(adjustment.Saturation) > 100
                    || !double.IsFinite(adjustment.Lightness) || Math.Abs(adjustment.Lightness) > 100)
                    return false;
            foreach (var band in hsv.Bands.Values)
            {
                var values = new[] { band.FalloffStart, band.RangeStart, band.RangeEnd, band.FalloffEnd };
                if (!values.All(double.IsFinite)) return false;
            }
            if (Levels.Ranges.Length != 4 || !Levels.Ranges.All(r => r == r.Normalized) || !Curves.IsValid)
                return false;
            if ((ExposureSettings?.IsValid ?? true) == false
                || (GradientMapSettings?.IsValid ?? true) == false
                || (GrainSettings?.IsValid ?? true) == false
                || (BlackWhiteSettings?.IsValid ?? true) == false
                || (ColorBalanceSettings?.IsValid ?? true) == false)
                return false;
            return double.IsFinite(GaussianRadius) && GaussianRadius is >= 0.1 and <= 250
                && double.IsFinite(ResolvedMotionAngle) && ResolvedMotionAngle is >= -90 and <= 90
                && double.IsFinite(ResolvedMotionDistance) && ResolvedMotionDistance is >= 1 and <= 2000
                && double.IsFinite(ResolvedNoiseAmount) && ResolvedNoiseAmount is >= 0.1 and <= 400;
        }
    }
}
