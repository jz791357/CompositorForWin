namespace Compositor.Document;

/// Snapshot-based undo/redo (upstream DocumentHistory.swift): each entry keeps whole
/// document states whose pixel buffers are shared (layers replace arrays on edit), with
/// an entry cap and a retained-pixel budget that drops the oldest history.
public sealed class DocumentHistory
{
    private sealed record Snapshot(CanvasDocument? Document, Guid? ActiveLayerID, Guid Revision);

    private sealed record Entry(string Name, Snapshot Before, Snapshot After);

    private readonly List<Entry> _past = [];
    private readonly List<Entry> _future = [];
    private Guid _revision = Guid.NewGuid();
    private Guid? _savedRevision;
    private Snapshot? _pending;
    private string _pendingName = "Edit";
    private int _depth;

    public int EntryLimit { get; }
    public long RetainedByteLimit { get; }

    public DocumentHistory(int entryLimit = 100, long retainedByteLimit = 256 * 1024 * 1024)
    {
        EntryLimit = Math.Max(0, entryLimit);
        RetainedByteLimit = Math.Max(0, retainedByteLimit);
        _savedRevision = _revision;
    }

    public bool CanUndo => _depth == 0 && _past.Count > 0;
    public bool CanRedo => _depth == 0 && _future.Count > 0;
    public string UndoName => _past.Count > 0 ? _past[^1].Name : "";
    public string RedoName => _future.Count > 0 ? _future[^1].Name : "";
    public bool IsModified => _revision != _savedRevision;
    public int UndoCount => _past.Count;

    public void MarkSaved() => _savedRevision = _revision;
    /// A save of `saved` finished (background-save semantics from upstream).
    public void MarkSaved(Guid saved) => _savedRevision = saved;
    public Guid CurrentRevision => _revision;

    public void Reset()
    {
        _past.Clear();
        _future.Clear();
        _pending = null;
        _depth = 0;
        _revision = Guid.NewGuid();
        _savedRevision = _revision;
    }

    public void Begin(string name, CanvasDocument? document, Guid? selection)
    {
        if (_depth == 0)
        {
            // Swift hands `document` in by value; freeze a copy so later in-place
            // edits cannot rewrite what "before" looked like (pixels stay shared).
            _pending = new Snapshot(document?.DeepCopy(), selection, _revision);
            _pendingName = name;
        }
        _depth++;
    }

    public void End(CanvasDocument? document, Guid? selection)
    {
        if (_depth == 0) return;
        _depth--;
        if (_depth > 0) return;
        var before = _pending;
        _pending = null;
        if (before is null) return;
        var after = new Snapshot(document?.DeepCopy(), selection, Guid.NewGuid());
        if (Equals(before.Document, after.Document) && before.ActiveLayerID == selection) return;   // no-op
        var entry = new Entry(_pendingName, before, after);
        _past.Add(entry);
        _future.Clear();
        _revision = after.Revision;
        Trim(after.Document);
    }

    public void Abort()
    {
        if (_depth == 0) return;
        _depth--;
        if (_depth == 0) _pending = null;
    }

    public (CanvasDocument? Document, Guid? Selection)? Undo()
    {
        if (!CanUndo) return null;
        var entry = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        _future.Add(entry);
        _revision = entry.Before.Revision;
        return (entry.Before.Document, entry.Before.ActiveLayerID);
    }

    public (CanvasDocument? Document, Guid? Selection)? Redo()
    {
        if (!CanRedo) return null;
        var entry = _future[^1];
        _future.RemoveAt(_future.Count - 1);
        _past.Add(entry);
        _revision = entry.After.Revision;
        return (entry.After.Document, entry.After.ActiveLayerID);
    }

    /// Whole-document equality the way Swift's CanvasDocument == compares layers by
    /// value while sharing pixel buffers: same layer list with same field values.
    internal static bool Equals(CanvasDocument? a, CanvasDocument? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Id != b.Id || a.Width != b.Width || a.Height != b.Height || a.Resolution != b.Resolution) return false;
        if (a.Guides.Count != b.Guides.Count || !a.Guides.SequenceEqual(b.Guides)) return false;
        if (a.Layers.Count != b.Layers.Count) return false;
        for (var i = 0; i < a.Layers.Count; i++)
        {
            var x = a.Layers[i];
            var y = b.Layers[i];
            if (x.Id != y.Id || x.Name != y.Name || x.IsVisible != y.IsVisible
                || !x.Transform.Equals(y.Transform)
                || !ReferenceEquals(x.Pixels, y.Pixels) && !SamePixels(x, y)
                || x.Width != y.Width || x.Height != y.Height
                || x.ParentID != y.ParentID || x.IsGroup != y.IsGroup
                || x.Opacity != y.Opacity || x.BlendMode != y.BlendMode
                || x.MaskSourceID != y.MaskSourceID)
                return false;
            if ((x.Mask is null) != (y.Mask is null)) return false;
            if (x.Mask is { } xm && y.Mask is { } ym
                && (xm.Enabled != ym.Enabled || xm.Width != ym.Width || xm.Height != ym.Height
                    || !ReferenceEquals(xm.Gray, ym.Gray) && !xm.Gray.AsSpan().SequenceEqual(ym.Gray)))
                return false;
        }
        return true;
    }

    private static bool SamePixels(ImageLayer x, ImageLayer y) =>
        x.Pixels?.Length == y.Pixels?.Length && x.Pixels!.AsSpan().SequenceEqual(y.Pixels);

    /// Pixel bytes history holds beyond what the current document references — the
    /// budget that drops old entries (upstream retainedBytes).
    public long RetainedBytes(CanvasDocument? current)
    {
        var live = Buffers(current);
        long total = 0;
        var seen = new HashSet<byte[]>(ReferenceEquality);
        foreach (var entry in _past.Concat(_future))
        {
            if (_pending is not null) break;
            foreach (var buffer in Buffers(entry.Before.Document).Concat(Buffers(entry.After.Document)))
                if (seen.Add(buffer) && !live.Contains(buffer))
                    total += buffer.Length;
        }
        if (_pending is { } pending)
            foreach (var buffer in Buffers(pending.Document))
                if (seen.Add(buffer) && !live.Contains(buffer))
                    total += buffer.Length;
        return total;
    }

    private static HashSet<byte[]> Buffers(CanvasDocument? document)
    {
        var set = new HashSet<byte[]>(ReferenceEquality);
        if (document is null) return set;
        foreach (var layer in document.Layers)
        {
            if (layer.Pixels is { } pixels) set.Add(pixels);
            if (layer.Mask is { } mask) set.Add(mask.Gray);
        }
        return set;
    }

    private static ReferenceEqualityComparer ReferenceEquality { get; } = ReferenceEqualityComparer.Instance;

    private void Trim(CanvasDocument? current)
    {
        while (_past.Count > EntryLimit && _past.Count > 0)
            _past.RemoveAt(0);
        while (_past.Count > 0 && RetainedBytes(current) > RetainedByteLimit)
            _past.RemoveAt(0);
    }
}
