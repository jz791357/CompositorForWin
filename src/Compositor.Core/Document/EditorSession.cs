using Compositor.IO;
namespace Compositor.Document;

// Upstream EditorSession.swift, core model half: the open document, the active layer
// and the edit history. UI-bound state (tools, panels, importing dialogs) arrives with
// the M1.5 shell wiring; the operations here already follow upstream's rules — blank
// naming, no-op edits leave no history, transactions group, imports/dialogs gate undo.

public sealed partial class EditorSession
{
    public CanvasDocument? Document { get; private set; }
    public Guid? ActiveLayerID { get; private set; }
    public DocumentHistory History { get; } = new();

    // Undo gates (upstream blocks history during imports and modal dialogs).
    public bool IsImporting { get; set; }
    public bool ShowsNewDocument { get; set; }
    public bool ShowsImporter { get; set; }

    public bool IsModified => History.IsModified;
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    public event Action? Changed;

    public void BeginEdit(string name) => History.Begin(name, Document, ActiveLayerID);

    public void EndEdit() => History.End(Document, ActiveLayerID);

    private void Mutate(string name, Action body)
    {
        // History freezes before/after copies with shared pixel buffers, matching
        // Swift's value-typed documents; the edit itself mutates in place.
        BeginEdit(name);
        body();
        EndEdit();
        Changed?.Invoke();
    }

    private bool HistoryGated => IsImporting || ShowsNewDocument || ShowsImporter;

    /// Replaces the document with a loaded project (upstream openProject): fresh
    /// history, selection restored from the manifest.
    public void LoadSnapshot(ProjectSnapshot snapshot)
    {
        Document = CanvasDocument.FromSnapshot(snapshot);
        ActiveLayerID = snapshot.Manifest.ActiveLayerID;
        History.Reset();
        History.MarkSaved();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (HistoryGated || !History.CanUndo) return;
        var restored = History.Undo();
        if (restored is { } state)
        {
            Document = state.Document;
            ActiveLayerID = state.Selection;
            Changed?.Invoke();
        }
    }

    public void Redo()
    {
        if (HistoryGated || !History.CanRedo) return;
        var restored = History.Redo();
        if (restored is { } state)
        {
            Document = state.Document;
            ActiveLayerID = state.Selection;
            Changed?.Invoke();
        }
    }

    public void CreateDocument(int width, int height, double resolution = 72) =>
        Mutate("New Canvas", () =>
        {
            Document = new CanvasDocument { Id = Guid.NewGuid(), Width = width, Height = height, Resolution = resolution };
            ActiveLayerID = null;
        });

    private string UntitledName()
    {
        var number = Document!.Layers.Count + 1;
        while (Document.Layers.Any(l => l.Name == $"Layer {number}")) number++;
        return $"Layer {number}";
    }

    public void AddBlankLayer() =>
        Mutate("New Layer", () =>
        {
            if (Document is null) return;
            var layer = ImageLayer.Blank(Guid.NewGuid(), Document.Width, Document.Height, UntitledName());
            var index = ActiveLayerID is { } active
                ? Document.Layers.FindIndex(l => l.Id == active) + 1
                : Document.Layers.Count;
            Document.Layers.Insert(index, layer);
            ActiveLayerID = layer.Id;
        });

    public void RenameLayer(Guid id, string to) =>
        Mutate("Rename Layer", () =>
        {
            var layer = Document?.Layers.FirstOrDefault(l => l.Id == id);
            if (layer is null || string.IsNullOrWhiteSpace(to)) return;   // blank names don't count
            layer.Name = to;
        });

    public void ToggleLayerVisibility(Guid id) =>
        Mutate("Toggle Visibility", () =>
        {
            var layer = Document?.Layers.FirstOrDefault(l => l.Id == id);
            if (layer is not null) layer.IsVisible = !layer.IsVisible;
        });

    public void ReorderLayers(int fromIndex, int toIndex) =>
        Mutate("Reorder Layers", () =>
        {
            var layers = Document?.Layers;
            if (layers is null || fromIndex < 0 || fromIndex >= layers.Count) return;
            var moving = layers[fromIndex];
            layers.RemoveAt(fromIndex);
            layers.Insert(Math.Clamp(toIndex, 0, layers.Count), moving);
        });

    public void MoveActiveLayer(int by) =>
        Mutate("Reorder Layers", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var index = layers.FindIndex(l => l.Id == id);
            if (index < 0) return;
            var target = Math.Clamp(index + by, 0, layers.Count - 1);
            if (target == index) return;
            var moving = layers[index];
            layers.RemoveAt(index);
            layers.Insert(target, moving);
        });

    public void DeleteActiveLayer() =>
        Mutate("Delete Layer", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var index = layers.FindIndex(l => l.Id == id);
            if (index < 0) return;
            layers.RemoveAt(index);
            ActiveLayerID = layers.Count > 0 ? layers[Math.Min(index, layers.Count - 1)].Id : null;
        });

    public void SetLayerOpacity(Guid id, double opacity) =>
        Mutate("Layer Opacity", () =>
        {
            var layer = Document?.Layers.FirstOrDefault(l => l.Id == id);
            if (layer is not null) layer.Opacity = Math.Clamp(opacity, 0, 1);
        });

    public void SetLayerBlendMode(Guid id, LayerBlendMode mode) =>
        Mutate("Blend Mode", () =>
        {
            var layer = Document?.Layers.FirstOrDefault(l => l.Id == id);
            if (layer is not null) layer.BlendMode = mode;
        });

    public void GroupSelectedLayers() =>
        Mutate("Group Layers", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var index = layers.FindIndex(l => l.Id == id);
            if (index < 0) return;
            var folder = new ImageLayer
            {
                Id = Guid.NewGuid(), Name = "Folder", IsGroup = true,
                Transform = new LayerTransform(new CGPoint(0, 0), new CGSize(Document.Width, Document.Height)),
                Width = Document.Width, Height = Document.Height,
            };
            var member = layers[index];
            layers.Insert(index, folder);
            member.ParentID = folder.Id;
        });

    public void UngroupLayers() =>
        Mutate("Ungroup Layers", () =>
        {
            if (Document?.Layers is not { } layers) return;
            for (var i = layers.Count - 1; i >= 0; i--)
            {
                if (layers[i].IsGroup is not true) continue;
                var folder = layers[i];
                foreach (var child in layers.Where(l => l.ParentID == folder.Id).ToList())
                    child.ParentID = folder.ParentID;
                layers.RemoveAt(i);
            }
        });
}

// Stroke and transform-drag editing (upstream EditorSession+Brush / TransformEdit, M1
// shape). Strokes copy the pixel buffer on first touch so history snapshots share the
// untouched one; drags preview without history and commit once on release.

public enum StrokeTarget { LayerPixels, LayerMask }

public sealed partial class EditorSession
{
    public Rendering.BrushEngine.BrushSettings Brush { get; set; } = new(Size: 24, Hardness: 0.8, Opacity: 1);
    public bool PaintingMaskWhite { get; set; } = true;
    private StrokeTarget _strokeTarget = StrokeTarget.LayerPixels;
    private bool _strokeTouched;

    public StrokeTarget StrokeTargetValue => _strokeTarget;

    public void SetStrokeTarget(StrokeTarget target) => _strokeTarget = target;

    /// A stroke begins on the active layer (or its mask); the whole stroke is one
    /// history entry.
    public void BeginStroke()
    {
        if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
        var layer = layers.FirstOrDefault(l => l.Id == id);
        if (layer is null) return;
        BeginEdit(_strokeTarget == StrokeTarget.LayerPixels ? "Paint" : "Paint Mask");
        _strokeTouched = false;
        // Copy-on-first-touch keeps untouched strokes a no-op for history.
    }

    private bool EnsureStrokeBuffer(ImageLayer layer)
    {
        if (_strokeTarget == StrokeTarget.LayerMask)
        {
            if (layer.Mask is null)
            {
                layer.Mask = new LayerMaskBuffer
                {
                    Width = layer.Width, Height = layer.Height,
                    Gray = new byte[layer.Width * layer.Height],   // black = hides nothing yet
                };
                // A brand-new mask starts revealing; painting decides its content.
                Array.Fill(layer.Mask.Gray, (byte)0);
            }
            if (!_strokeTouched)
            {
                layer.Mask = new LayerMaskBuffer
                {
                    Width = layer.Mask.Width, Height = layer.Mask.Height,
                    Gray = (byte[])layer.Mask.Gray.Clone(), Enabled = layer.Mask.Enabled,
                };
                _strokeTouched = true;
            }
            return layer.Mask is not null;
        }
        if (layer.Pixels is null) return false;
        if (!_strokeTouched)
        {
            layer.Pixels = (byte[])layer.Pixels.Clone();
            _strokeTouched = true;
        }
        return true;
    }

    public void ExtendStroke(CGPoint documentFrom, CGPoint documentTo)
    {
        if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
        var layer = layers.FirstOrDefault(l => l.Id == id);
        if (layer is null || !EnsureStrokeBuffer(layer)) return;
        // Document → layer-local pixel coordinates (M1: unrotated placement).
        var transform = layer.Transform;
        var scaleX = transform.Size.Width / layer.Width;
        var scaleY = transform.Size.Height / layer.Height;
        CGPoint Local(CGPoint p) => new(
            (p.X - transform.Origin.X) / Math.Max(scaleX, 1e-9),
            (p.Y - transform.Origin.Y) / Math.Max(scaleY, 1e-9));
        if (_strokeTarget == StrokeTarget.LayerMask && layer.Mask is { } mask)
        {
            Rendering.BrushEngine.PaintMaskSegment(mask.Gray, mask.Width, mask.Height,
                Local(documentFrom), Local(documentTo), Brush, PaintingMaskWhite);
        }
        else if (layer.Pixels is { } pixels)
        {
            var brush = Tool == Tool.Eraser ? Brush with { Erase = true } : Brush;
            Rendering.BrushEngine.PaintSegment(pixels, layer.Width, layer.Height,
                Local(documentFrom), Local(documentTo), brush);
        }
        Changed?.Invoke();
    }

    public void EndStroke()
    {
        EndEdit();
        Changed?.Invoke();
    }

    // --- transform dragging (Move tool) ---

    private LayerTransform? _dragStart;
    private Guid _dragLayer;

    public bool BeginLayerDrag(CGPoint documentPoint)
    {
        if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return false;
        var layer = layers.FirstOrDefault(l => l.Id == id);
        if (layer is null) return false;
        BeginEdit("Move Layer");
        _dragStart = layer.Transform;
        _dragLayer = id;
        return true;
    }

    public void DragLayerBy(double dx, double dy)
    {
        if (_dragStart is not { } start || Document?.Layers is not { } layers) return;
        var layer = layers.FirstOrDefault(l => l.Id == _dragLayer);
        if (layer is null) return;
        layer.Transform = start with
        {
            Origin = new CGPoint(Math.Round(start.Origin.X + dx), Math.Round(start.Origin.Y + dy)),
        };
        Changed?.Invoke();
    }

    public void EndLayerDrag()
    {
        _dragStart = null;
        EndEdit();
        Changed?.Invoke();
    }

    // --- mask management ---

    public void AddRevealingMask()
    {
        Mutate("Add Mask", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var layer = layers.FirstOrDefault(l => l.Id == id);
            if (layer?.Pixels is null || layer.Mask is not null) return;
            var gray = new byte[layer.Width * layer.Height];
            Array.Fill(gray, (byte)255);   // reveals everything until painted
            layer.Mask = new LayerMaskBuffer { Width = layer.Width, Height = layer.Height, Gray = gray };
        });
    }

    public void RemoveMask()
    {
        Mutate("Remove Mask", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var layer = layers.FirstOrDefault(l => l.Id == id);
            if (layer?.Mask is null) return;
            layer.Mask = null;
            if (_strokeTarget == StrokeTarget.LayerMask) _strokeTarget = StrokeTarget.LayerPixels;
        });
    }

    public void ToggleMaskEnabled()
    {
        Mutate("Toggle Mask", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var mask = layers.FirstOrDefault(l => l.Id == id)?.Mask;
            if (mask is not null) mask.Enabled = !mask.Enabled;
        });
    }
}

// Upstream EditorTool (M1 subset; selection and the rest arrive in M2).
public enum Tool { Move, Brush, Eraser }

public sealed partial class EditorSession
{
    public Tool Tool { get; set; } = Tool.Move;
}

public sealed partial class EditorSession
{
    /// Selection is session state (upstream keeps it out of history).
    public void SelectLayer(Guid id)
    {
        if (Document?.Layers.Any(l => l.Id == id) != true) return;
        ActiveLayerID = id;
        Changed?.Invoke();
    }

    public void DuplicateLayer(Guid id) =>
        Mutate("Duplicate Layer", () =>
        {
            if (Document?.Layers is not { } layers) return;
            var index = layers.FindIndex(l => l.Id == id);
            if (index < 0) return;
            var source = layers[index];
            var copy = DuplicateOf(source);
            layers.Insert(index + 1, copy);
            ActiveLayerID = copy.Id;
        });
}

public sealed partial class EditorSession
{
    /// Closes the open project: no document, clean history (upstream closeProject).
    public void CloseDocument()
    {
        Document = null;
        ActiveLayerID = null;
        History.Reset();
        History.MarkSaved();
        Changed?.Invoke();
    }

    /// Imports decoded straight-RGBA pixels as a new layer (upstream importImages for
    /// one file, M1 shape): sized to fit within the canvas, active on top.
    public void ImportLayerPixels(string name, int width, int height, byte[] rgba, CGSize? placement = null)
    {
        Mutate("Import Image", () =>
        {
            if (Document is null) return;
            var layer = new ImageLayer
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(name) ? "Image" : name,
                Transform = new LayerTransform(new CGPoint(0, 0), placement ?? new CGSize(width, height)),
                Pixels = rgba,
                Width = width,
                Height = height,
            };
            Document.Layers.Add(layer);
            ActiveLayerID = layer.Id;
        });
    }

    public void FillActiveLayer(byte r, byte g, byte b) =>
        Mutate("Fill", () =>
        {
            if (Document?.ActiveLayer is not { } layer || layer.Pixels is not { } pixels) return;
            var copy = new byte[pixels.Length];
            for (var i = 0; i < layer.Width * layer.Height; i++)
            {
                copy[i * 4] = r; copy[i * 4 + 1] = g; copy[i * 4 + 2] = b; copy[i * 4 + 3] = 255;
            }
            layer.Pixels = copy;
        });

    public void ResizeCanvas(int width, int height) =>
        Mutate("Canvas Size", () =>
        {
            if (Document is null) return;
            var replacement = new CanvasDocument
            {
                Id = Document.Id, Width = width, Height = height, Resolution = Document.Resolution,
                ActiveLayerID = ActiveLayerID, Guides = Document.Guides,
                Layers = Document.Layers.Select(CanvasDocument.LayerCopy).ToList(),
            };
            Document = replacement;   // layers keep their pixels; placement unchanged
        });

    public void FlipCanvas(bool horizontal) =>
        Mutate("Flip Canvas", () =>
        {
            if (Document?.Layers is not { } layers) return;
            foreach (var layer in layers)
                layer.Transform = layer.Transform with { FlipX = horizontal ? !layer.Transform.FlipX : layer.Transform.FlipX,
                                                         FlipY = horizontal ? layer.Transform.FlipY : !layer.Transform.FlipY };
        });

    public void MergeActiveDown() =>
        Mutate("Merge Down", () =>
        {
            if (Document?.Layers is not { } layers || ActiveLayerID is not { } id) return;
            var index = layers.FindIndex(l => l.Id == id);
            if (index <= 0) return;
            var upper = layers[index];
            var lower = layers[index - 1];
            if (upper.IsGroup || lower.IsGroup || upper.Pixels is null || lower.Pixels is null) return;
            // Render just the pair (upper over lower) onto the lower's extent.
            var pair = new CanvasDocument
            {
                Id = Guid.NewGuid(), Width = Math.Max(lower.Width, upper.Width), Height = Math.Max(lower.Height, upper.Height),
                Layers = [CanvasDocument.LayerCopy(lower), CanvasDocument.LayerCopy(upper)],
            };
            var width = Math.Max(lower.Width, upper.Width);
            var height = Math.Max(lower.Height, upper.Height);
            var pixels = Compositor.Rendering.LayerRenderer.Render(pair);
            lower.Pixels = pixels;
            // The merged extent keeps the lower layer's placement.
            lower.Transform = new LayerTransform(new CGPoint(0, 0), new CGSize(width, height))
            {
                Rotation = 0, FlipX = false, FlipY = false, Sampling = lower.Transform.Sampling,
            };
            var lowerType = lower.GetType();
            layers.RemoveAt(index);
            ActiveLayerID = lower.Id;
        });

    public void ClearGuides() =>
        Mutate("Clear Guides", () =>
        {
            if (Document is not null) Document.Guides.Clear();
        });
}

public sealed partial class EditorSession
{
    private static ImageLayer DuplicateOf(ImageLayer source)
    {
        var copy = CanvasDocument.LayerCopy(source);
        return new ImageLayer
        {
            Id = Guid.NewGuid(),
            Name = $"{source.Name} copy",
            IsVisible = copy.IsVisible,
            Transform = copy.Transform,
            Pixels = copy.Pixels,
            Width = copy.Width,
            Height = copy.Height,
            ParentID = copy.ParentID,
            IsGroup = copy.IsGroup,
            Opacity = copy.Opacity,
            BlendMode = copy.BlendMode,
            Mask = copy.Mask,
            MaskSourceID = copy.MaskSourceID,
            Adjustment = copy.Adjustment,
        };
    }
}
