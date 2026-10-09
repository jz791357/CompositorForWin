using System.Text.Json.Serialization;

namespace Compositor.Document;

// Upstream LayerAppearance.swift. The CoreGraphics/CoreImage mappings (cgMode,
// coreImageFilter, needsSurface) land with the renderer in later milestones; the
// serialized raw values are the format contract.

public enum LayerBlendMode
{
    [JsonPropertyName("Normal")] Normal,
    [JsonPropertyName("Darken")] Darken,
    [JsonPropertyName("Multiply")] Multiply,
    [JsonPropertyName("Color Burn")] ColorBurn,
    [JsonPropertyName("Linear Burn")] LinearBurn,
    [JsonPropertyName("Lighten")] Lighten,
    [JsonPropertyName("Screen")] Screen,
    [JsonPropertyName("Color Dodge")] ColorDodge,
    [JsonPropertyName("Linear Dodge (Add)")] LinearDodge,
    [JsonPropertyName("Overlay")] Overlay,
    [JsonPropertyName("Soft Light")] SoftLight,
    [JsonPropertyName("Hard Light")] HardLight,
    [JsonPropertyName("Vivid Light")] VividLight,
    [JsonPropertyName("Linear Light")] LinearLight,
    [JsonPropertyName("Pin Light")] PinLight,
    [JsonPropertyName("Hard Mix")] HardMix,
    [JsonPropertyName("Difference")] Difference,
    [JsonPropertyName("Exclusion")] Exclusion,
    [JsonPropertyName("Subtract")] Subtract,
    [JsonPropertyName("Divide")] Divide,
    [JsonPropertyName("Hue")] Hue,
    [JsonPropertyName("Saturation")] Saturation,
    [JsonPropertyName("Color")] Color,
    [JsonPropertyName("Luminosity")] Luminosity,
}

public static class LayerBlendModes
{
    /// Photoshop's grouping: darkening modes, lightening, contrast, comparative, component.
    public static readonly LayerBlendMode[][] Groups =
    [
        [LayerBlendMode.Normal],
        [LayerBlendMode.Darken, LayerBlendMode.Multiply, LayerBlendMode.ColorBurn, LayerBlendMode.LinearBurn],
        [LayerBlendMode.Lighten, LayerBlendMode.Screen, LayerBlendMode.ColorDodge, LayerBlendMode.LinearDodge],
        [LayerBlendMode.Overlay, LayerBlendMode.SoftLight, LayerBlendMode.HardLight, LayerBlendMode.VividLight,
         LayerBlendMode.LinearLight, LayerBlendMode.PinLight, LayerBlendMode.HardMix],
        [LayerBlendMode.Difference, LayerBlendMode.Exclusion, LayerBlendMode.Subtract, LayerBlendMode.Divide],
        [LayerBlendMode.Hue, LayerBlendMode.Saturation, LayerBlendMode.Color, LayerBlendMode.Luminosity],
    ];
}
