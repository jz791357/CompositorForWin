using System.Text.Json.Serialization;

namespace Compositor.Document;

// Upstream LayerTransform.swift, model half. The interactive editing types
// (TransformEdit, TransformDrag, TransformSnap) arrive with the M2 transform tools.

public enum LayerSampling
{
    [JsonPropertyName("Nearest")] Nearest,
    [JsonPropertyName("Smooth")] Smooth,
    [JsonPropertyName("High quality")] High,
}

/// Unrotated bounds in document pixels; rotation is clockwise around their center.
public readonly struct LayerTransform(CGPoint origin, CGSize size) : IEquatable<LayerTransform>
{
    public CGPoint Origin { get; init; } = origin;
    public CGSize Size { get; init; } = size;
    public double Rotation { get; init; }
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }
    public LayerSampling Sampling { get; init; } = LayerSampling.High;

    public CGPoint Center => new(Origin.X + Size.Width / 2, Origin.Y + Size.Height / 2);
    public double Radians => Rotation % 360 * Math.PI / 180;
    public bool IsValid =>
        new[] { Origin.X, Origin.Y, Size.Width, Size.Height, Rotation }.All(double.IsFinite)
        && Size.Width is >= 1 and <= 300_000 && Size.Height is >= 1 and <= 300_000
        && Math.Abs(Origin.X) <= 1_000_000 && Math.Abs(Origin.Y) <= 1_000_000;

    public bool Equals(LayerTransform other) =>
        Origin == other.Origin && Size == other.Size && Rotation == other.Rotation
        && FlipX == other.FlipX && FlipY == other.FlipY && Sampling == other.Sampling;
    public override bool Equals(object? obj) => obj is LayerTransform other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Origin, Size, Rotation, FlipX, FlipY, Sampling);
}
