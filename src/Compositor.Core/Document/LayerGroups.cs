using Compositor.IO;

namespace Compositor.Document;

// Upstream LayerGroups.swift — hierarchy traversal and validation.

public static class LayerHierarchy
{
    public readonly record struct Entry(ProjectLayerRecord Layer, int Depth, bool Visible);

    public static List<Entry> Entries(IReadOnlyList<ProjectLayerRecord> layers, bool topFirst = false, HashSet<Guid>? collapsed = null)
    {
        var children = new Dictionary<Guid?, List<ProjectLayerRecord>>();
        List<ProjectLayerRecord> roots = [];
        foreach (var layer in layers)
        {
            if (layer.ParentID is { } parent)
            {
                if (!children.TryGetValue(parent, out var siblings)) children[parent] = siblings = [];
                siblings.Add(layer);
            }
            else roots.Add(layer);
        }
        var result = new List<Entry>();
        void Visit(Guid? parent, int depth, bool visible)
        {
            if (depth > 64) return;
            var siblings = parent is { } id ? children.GetValueOrDefault(id) : roots;
            if (siblings is null) return;
            var order = topFirst ? Enumerable.Reverse(siblings) : siblings;
            foreach (var layer in order)
            {
                var effective = visible && layer.IsVisible;
                result.Add(new Entry(layer, depth, effective));
                if (layer.IsGroup == true && (collapsed?.Contains(layer.Id) != true))
                    Visit(layer.Id, depth + 1, effective);
            }
        }
        Visit(null, 0, true);
        return result;
    }

    public static List<ProjectLayerRecord> VisibleLayers(IReadOnlyList<ProjectLayerRecord> layers) =>
        Entries(layers).Where(e => e.Visible && e.Layer.IsGroup != true).Select(e => e.Layer).ToList();

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
