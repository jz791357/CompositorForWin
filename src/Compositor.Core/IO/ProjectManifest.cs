using System.Text.Json.Serialization;
using Compositor.Document;

namespace Compositor.IO;

// Upstream ProjectManifest + ProjectLayerRecord (ProjectStore.swift). Every property name
// matches Swift's synthesized Codable keys exactly — this JSON is the cross-platform
// contract. `required` mirrors Swift's `let` fields, which refuse to decode when absent;
// records mirror Swift structs' value semantics (usable with `with` expressions).

public sealed record ProjectManifest
{
    /// The format version new saves write.
    public const int Current = 11;
    public const int SupportedMin = 1;
    /// Every version load accepts (1...Current).
    public static bool Supports(int version) => version is >= SupportedMin and <= Current;

    public string Format { get; init; } = "com.compositor.project";
    public int Version { get; init; } = Current;
    public string ColorSpace { get; init; } = "sRGB";
    /// Older version-1 projects default to 72 pixels/inch.
    public double? Resolution { get; init; }
    public required Guid DocumentID { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public Guid? ActiveLayerID { get; init; }
    public required ProjectLayerRecord[] Layers { get; init; }
    /// Alignment guides. Missing on versions 1–7.
    public CanvasGuide[]? Guides { get; init; }
}

public sealed record ProjectLayerRecord : Compositor.Document.ILayerRecordView
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool IsVisible { get; init; }
    public required LayerTransform Transform { get; init; }
    public string? ImageFile { get; init; }
    public Guid? ParentID { get; init; }
    public bool? IsGroup { get; init; }
    public double? Opacity { get; init; }
    public LayerBlendMode? BlendMode { get; init; }
    public string? MaskFile { get; init; }
    public bool? MaskEnabled { get; init; }
    public Guid? MaskSourceID { get; init; }
    public LayerAdjustment? Adjustment { get; init; }
    /// A mask moved apart from its layer: where it sits on the document.
    public LayerTransform? MaskPlacement { get; init; }
    /// Null (older projects) is linked.
    public bool? MaskLinked { get; init; }
    /// A shape layer's shape, drawn again when the layer is scaled.
    public LayerShapeStyle? Shape { get; init; }
    /// The stroke and drop shadow drawn around the layer.
    public LayerEffects? Effects { get; init; }
    public LayerTextStyle? Text { get; init; }

    public double OpacityOrOne => Opacity ?? 1;

    // The runtime view of the optional group flag (the runtime layer uses a plain bool).
    bool Compositor.Document.ILayerRecordView.IsGroup => IsGroup == true;
}

/// A project's persisted state (upstream ProjectSnapshot).
public sealed class ProjectSnapshot
{
    public required ProjectManifest Manifest { get; init; }
    public required Dictionary<Guid, ImportedImage> Images { get; init; } = [];
    public Dictionary<Guid, ImportedImage> Masks { get; init; } = [];
}

/// One image asset (upstream ImportedImage). `Pixels` are straight RGBA8 for images
/// (4 bytes/pixel) and Gray8 for masks (1 byte/pixel); `Thumbnail` is ≤96px RGBA8.
public sealed class ImportedImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required PngPixelKind Kind { get; init; }
    public required byte[] Pixels { get; init; }
    public required byte[] Thumbnail { get; init; }
    public required string Name { get; init; }
    public byte[]? Encoded { get; init; }
}
