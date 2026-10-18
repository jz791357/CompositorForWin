namespace Compositor.Document;

// Upstream EditorSession.swift, core model half: the open document, the active layer
// and the edit history. UI-bound state (tools, panels, importing dialogs) arrives with
// the M1.5 shell wiring; the operations here already follow upstream's rules — blank
// naming, no-op edits leave no history, transactions group, imports/dialogs gate undo.

public sealed class EditorSession
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

    public void CreateDocument(int width, int height) =>
        Mutate("New Canvas", () =>
        {
            Document = new CanvasDocument { Id = Guid.NewGuid(), Width = width, Height = height };
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
