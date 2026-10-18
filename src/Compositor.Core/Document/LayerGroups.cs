using Compositor.IO;

namespace Compositor.Document;

// Common read shape of a layer for hierarchy traversal, implemented by both the
// manifest record and the runtime layer (upstream uses the same record view for both).
public interface ILayerRecordView
{
    Guid Id { get; }
    Guid? ParentID { get; }
    bool IsGroup { get; }
    bool IsVisible { get; }
}

// Upstream LayerGroups.swift — hierarchy traversal and validation.

public static class LayerHierarchy
{
    public readonly record struct Entry<T>(T Layer, int Depth, bool Visible) where T : ILayerRecordView;

    public static List<Entry<T>> Entries<T>(IReadOnlyList<T> layers, bool topFirst = false, HashSet<Guid>? collapsed = null)
        where T : ILayerRecordView
    {
        var children = new Dictionary<Guid, List<T>>();
        List<T> roots = [];
        foreach (var layer in layers)
        {
            if (layer.ParentID is { } parent)
            {
                if (!children.TryGetValue(parent, out var siblings)) children[parent] = siblings = [];
                siblings.Add(layer);
            }
            else roots.Add(layer);
        }
        var result = new List<Entry<T>>();
        void Visit(List<T> siblings, int depth, bool visible)
        {
            if (depth > 64) return;
            var order = topFirst ? Enumerable.Reverse(siblings) : siblings;
            foreach (var layer in order)
            {
                var effective = visible && layer.IsVisible;
                result.Add(new Entry<T>(layer, depth, effective));
                if (layer.IsGroup && (collapsed?.Contains(layer.Id) != true)
                    && children.TryGetValue(layer.Id, out var group))
                    Visit(group, depth + 1, effective);
            }
        }
        Visit(roots, 0, true);
        return result;
    }

    public static void Validate(IReadOnlyList<ProjectLayerRecord> layers)
    {
        var byID = new Dictionary<Guid, ProjectLayerRecord>();
        foreach (var layer in layers)
        {
            if (byID.ContainsKey(layer.Id) || (layer.IsGroup == true && layer.ImageFile != null))
                throw new ProjectException(ProjectError.Invalid);
            byID[layer.Id] = layer;
        }
        foreach (var layer in layers)
        {
            var seen = new HashSet<Guid> { layer.Id };
            var parent = layer.ParentID;
            while (parent is { } id)
            {
                if (seen.Count > 64 || !seen.Add(id) || !byID.TryGetValue(id, out var node) || node.IsGroup != true)
                    throw new ProjectException(ProjectError.Invalid);
                parent = node.ParentID;
            }
            if (layer.IsGroup == true && seen.Count > 64) throw new ProjectException(ProjectError.Invalid);
        }
    }
}
