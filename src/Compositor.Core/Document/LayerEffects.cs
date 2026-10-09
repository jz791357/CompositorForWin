namespace Compositor.Document;

// Upstream LayerEffects.swift, persisted shapes. GPU rendering arrives with M3.

public sealed class StrokeEffect
{
    public const double MaxSize = 500;
    public bool? Enabled { get; init; }                    // Missing in older projects means visible.
    public bool IsEnabled => Enabled ?? true;
    public double Size { get; init; } = 4;
    public double Red { get; init; }
    public double Green { get; init; }
    public double Blue { get; init; }
    public double Opacity { get; init; } = 1;
    public bool Inside { get; init; }
    public bool IsValid => double.IsFinite(Size) && Size is >= 0 and <= MaxSize
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1;
}

public sealed class ShadowEffect
{
    public bool? Enabled { get; init; }
    public bool IsEnabled => Enabled ?? true;
    public double Angle { get; init; } = 90;
    public double Distance { get; init; } = 20;
    public double Blur { get; init; } = 20;
    public double Red { get; init; }
    public double Green { get; init; }
    public double Blue { get; init; }
    public double Opacity { get; init; } = 0.5;
    public bool IsValid => new[] { Angle, Distance, Blur }.All(double.IsFinite)
        && Angle is >= -360 and <= 360 && Distance is >= 0 and <= 5000 && Blur is >= 0 and <= 500
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1;
}

public sealed class ColorOverlayEffect
{
    public bool? Enabled { get; init; }
    public bool IsEnabled => Enabled ?? true;
    public double Red { get; init; }
    public double Green { get; init; }
    public double Blue { get; init; }
    public double Opacity { get; init; } = 1;
    public bool IsValid => new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1;
}

public sealed class InnerShadowEffect
{
    public bool? Enabled { get; init; }
    public bool IsEnabled => Enabled ?? true;
    public double Angle { get; init; } = 90;
    public double Distance { get; init; } = 10;
    public double Blur { get; init; } = 20;
    public double Red { get; init; }
    public double Green { get; init; }
    public double Blue { get; init; }
    public double Opacity { get; init; } = 0.5;
    public bool IsValid => new[] { Angle, Distance, Blur }.All(double.IsFinite)
        && Angle is >= -360 and <= 360 && Distance is >= 0 and <= 5000 && Blur is >= 0 and <= 500
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1;
}

public sealed class OuterGlowEffect
{
    public bool? Enabled { get; init; }
    public bool IsEnabled => Enabled ?? true;
    public double Size { get; init; } = 20;
    public double Red { get; init; } = 1;
    public double Green { get; init; } = 1;
    public double Blue { get; init; } = 1;
    public double Opacity { get; init; } = 0.75;
    public bool IsValid => double.IsFinite(Size) && Size is >= 0 and <= 500
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1;
}

public sealed class InnerGlowEffect
{
    public bool? Enabled { get; init; }
    public bool IsEnabled => Enabled ?? true;
    public double Size { get; init; } = 10;
    public double Red { get; init; } = 1;
    public double Green { get; init; } = 1;
    public double Blue { get; init; } = 1;
    public double Opacity { get; init; } = 0.75;
    public bool IsValid => double.IsFinite(Size) && Size is >= 0 and <= 500
        && new[] { Red, Green, Blue }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        && double.IsFinite(Opacity) && Opacity is >= 0 and <= 1;
}

public sealed class LayerEffects
{
    public StrokeEffect? Stroke { get; init; }
    public ShadowEffect? Shadow { get; init; }
    public ColorOverlayEffect? ColorOverlay { get; init; }
    public InnerShadowEffect? InnerShadow { get; init; }
    public OuterGlowEffect? OuterGlow { get; init; }
    public InnerGlowEffect? InnerGlow { get; init; }
    public bool IsEmpty => Stroke is null && Shadow is null && ColorOverlay is null
        && InnerShadow is null && OuterGlow is null && InnerGlow is null;
    public bool IsValid => (Stroke?.IsValid ?? true) && (Shadow?.IsValid ?? true)
        && (ColorOverlay?.IsValid ?? true) && (InnerShadow?.IsValid ?? true)
        && (OuterGlow?.IsValid ?? true) && (InnerGlow?.IsValid ?? true);
}
