using System.Text.Json.Serialization;

namespace Compositor.Document;

// Upstream Guides.swift, persisted shape. The ruler/drag interactions arrive with M1.4.

public enum CanvasGuideAxis
{
    [JsonPropertyName("horizontal")] Horizontal,
    [JsonPropertyName("vertical")] Vertical,
}

public readonly struct CanvasGuide(Guid id, CanvasGuideAxis axis, double position)
{
    public Guid Id { get; init; } = id;
    public CanvasGuideAxis Axis { get; init; } = axis;
    /// Document pixels: Y for a horizontal guide, X for a vertical one.
    public double Position { get; init; } = position;

    public CanvasGuide Offset(double x, double y) => this with
    { Position = Position + (Axis == CanvasGuideAxis.Vertical ? x : y) };
}
