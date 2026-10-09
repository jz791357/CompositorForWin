using Compositor.IO;

namespace Compositor.Document;

// Upstream LiveLayerMask.swift, graph half: clipping-mask links (maskSourceID) must
// terminate, never cycle, and point at pixel layers. Session-side helpers arrive later.

public static class LiveMaskGraph
{
    public static void Validate(IReadOnlyList<ProjectLayerRecord> layers)
    {
        var records = new Dictionary<Guid, ProjectLayerRecord>();
        foreach (var layer in layers)
        {
            if (records.ContainsKey(layer.Id)) throw new ProjectException(ProjectError.Invalid);
            records[layer.Id] = layer;
        }
        foreach (var layer in layers)
        {
            var path = new HashSet<Guid>();
            Guid? current = layer.Id;
            while (current is { } id)
            {
                if (path.Count >= 256 || !path.Add(id) || !records.TryGetValue(id, out var record))
                    throw new ProjectException(ProjectError.Invalid);
                if (record.MaskSourceID is { } source)
                {
                    if (record.IsGroup == true
                        || !records.TryGetValue(source, out var target)
                        || target.IsGroup == true || target.Adjustment != null)
                        throw new ProjectException(ProjectError.Invalid);
                }
                current = record.MaskSourceID;
            }
        }
    }
}
