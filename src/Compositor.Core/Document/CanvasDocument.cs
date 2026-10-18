using Compositor.IO;

namespace Compositor.Document;

// Upstream CanvasDocument (in EditorSession.swift): the open project's identity, size,
// layer tree and guides. Layers are bottom-to-top; pixels are immutable byte buffers
// replaced on edit so undo snapshots share them without copying (the CGImage-sharing
// translation).

public sealed class CanvasDocument
{
    public required Guid Id { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public double Resolution { get; init; } = 72;
    public List<ImageLayer> Layers { get; set; } = [];
    public List<CanvasGuide> Guides { get; set; } = [];
    public Guid? ActiveLayerID { get; set; }

    public CGSize Size => new(Width, Height);
    public ImageLayer? ActiveLayer => Layers.FirstOrDefault(l => l.Id == ActiveLayerID);

    /// Swift documents are value types: every edit works on a copy. This clone copies
    /// the document and its layer/mask objects but SHARES the pixel buffers, so undo
    /// snapshots hold old pixels without duplicating them.
    public CanvasDocument DeepCopy() => new()
    {
        Id = Id,
        Width = Width,
        Height = Height,
        Resolution = Resolution,
        ActiveLayerID = ActiveLayerID,
        Guides = [.. Guides],
        Layers = Layers.Select(LayerCopy).ToList(),
    };

    internal static ImageLayer LayerCopy(ImageLayer layer) => new()
    {
        Id = layer.Id,
        Name = layer.Name,
        IsVisible = layer.IsVisible,
        Transform = layer.Transform,
        Pixels = layer.Pixels,
        Width = layer.Width,
        Height = layer.Height,
        ParentID = layer.ParentID,
        IsGroup = layer.IsGroup,
        Opacity = layer.Opacity,
        BlendMode = layer.BlendMode,
        Mask = layer.Mask is null ? null : new LayerMaskBuffer
        {
            Width = layer.Mask.Width,
            Height = layer.Mask.Height,
            Gray = layer.Mask.Gray,
            Enabled = layer.Mask.Enabled,
        },
        MaskSourceID = layer.MaskSourceID,
        Adjustment = layer.Adjustment,
    };

    /// Manifest + assets for saving (upstream ProjectSnapshot from the session).
    public ProjectSnapshot Snapshot()
    {
        var images = new Dictionary<Guid, ImportedImage>();
        var masks = new Dictionary<Guid, ImportedImage>();
        var records = new List<ProjectLayerRecord>();
        foreach (var layer in Layers)
        {
            records.Add(layer.Record());
            if (layer.Pixels is { } pixels)
                images[layer.Id] = new ImportedImage
                {
                    Width = layer.Width, Height = layer.Height, Kind = PngPixelKind.Rgba8,
                    Pixels = pixels, Thumbnail = [], Name = layer.Name,
                };
            if (layer.Mask is { } mask)
                masks[layer.Id] = new ImportedImage
                {
                    Width = mask.Width, Height = mask.Height, Kind = PngPixelKind.Gray8,
                    Pixels = mask.Gray, Thumbnail = [], Name = layer.Name,
                };
        }
        return new ProjectSnapshot
        {
            Manifest = new ProjectManifest
            {
                DocumentID = Id,
                Width = Width,
                Height = Height,
                Resolution = Resolution == 72 ? null : Resolution,
                ActiveLayerID = ActiveLayerID,
                Layers = [.. records],
                Guides = [.. Guides],
            },
            Images = images,
            Masks = masks,
        };
    }

    public static CanvasDocument FromSnapshot(ProjectSnapshot snapshot)
    {
        var manifest = snapshot.Manifest;
        var document = new CanvasDocument
        {
            Id = manifest.DocumentID,
            Width = manifest.Width,
            Height = manifest.Height,
            Resolution = manifest.Resolution ?? 72,
            ActiveLayerID = manifest.ActiveLayerID,
            Guides = [.. manifest.Guides ?? []],
        };
        foreach (var record in manifest.Layers)
        {
            var image = record.ImageFile is null ? null : snapshot.Images.GetValueOrDefault(record.Id);
            var mask = record.MaskFile is null ? null : snapshot.Masks.GetValueOrDefault(record.Id);
            document.Layers.Add(ImageLayer.FromRecord(record, image, mask));
        }
        return document;
    }
}

/// A runtime layer's raster mask: 8-bit gray, its own transform when unlinked.
public sealed class LayerMaskBuffer
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// Gray coverage, 1 byte per pixel: white reveals, black hides.
    public required byte[] Gray { get; init; }
    public bool Enabled { get; set; } = true;
}

// Upstream ImageLayer: pixels + transform + appearance + optional mask, group flag and
// clipping link. Pixel buffers are replaced (never mutated in place) so history
// snapshots share them.
public sealed class ImageLayer : ILayerRecordView
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public bool IsVisible { get; set; } = true;
    public required LayerTransform Transform { get; set; }
    /// Straight RGBA8, Width*Height*4 — null on groups and adjustment carriers.
    public byte[]? Pixels { get; set; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public Guid? ParentID { get; set; }
    public bool IsGroup { get; init; }
    public double Opacity { get; set; } = 1;
    public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;
    public LayerMaskBuffer? Mask { get; set; }
    public Guid? MaskSourceID { get; set; }
    public LayerAdjustment? Adjustment { get; set; }

    public ProjectLayerRecord Record() => new()
    {
        Id = Id,
        Name = Name,
        IsVisible = IsVisible,
        Transform = Transform,
        ImageFile = Pixels is null ? null : $"{Id.ToString("D").ToUpperInvariant()}.png",
        ParentID = ParentID,
        IsGroup = IsGroup ? true : null,
        Opacity = Opacity == 1 ? null : Opacity,
        BlendMode = BlendMode == LayerBlendMode.Normal ? null : BlendMode,
        MaskFile = Mask is null ? null : $"{Id.ToString("D").ToUpperInvariant()}.mask.png",
        MaskEnabled = Mask is null || Mask.Enabled ? null : false,
        MaskSourceID = MaskSourceID,
        Adjustment = Adjustment,
    };

    public static ImageLayer FromRecord(ProjectLayerRecord record, ImportedImage? image, ImportedImage? mask) => new()
    {
        Id = record.Id,
        Name = record.Name,
        IsVisible = record.IsVisible,
        Transform = record.Transform,
        Pixels = image?.Pixels,
        Width = image?.Width ?? (int)record.Transform.Size.Width,
        Height = image?.Height ?? (int)record.Transform.Size.Height,
        ParentID = record.ParentID,
        IsGroup = record.IsGroup == true,
        Opacity = record.Opacity ?? 1,
        BlendMode = record.BlendMode ?? LayerBlendMode.Normal,
        Mask = mask is null ? null : new LayerMaskBuffer
        {
            Width = mask.Width,
            Height = mask.Height,
            Gray = mask.Pixels,
            Enabled = record.MaskEnabled ?? true,
        },
        MaskSourceID = record.MaskSourceID,
        Adjustment = record.Adjustment,
    };

    public static ImageLayer Blank(Guid id, int width, int height, string name = "Layer")
    {
        var transform = new LayerTransform(new CGPoint(0, 0), new CGSize(width, height));
        return new ImageLayer
        {
            Id = id, Name = name, Transform = transform,
            Pixels = new byte[width * height * 4], Width = width, Height = height,
        };
    }
}
