using System.IO;
using Compositor.Document;
using Compositor.IO;

namespace Compositor.Editing;

// Upstream ProjectController: file-level commands (new/open/save/export/recent) and
// the modified/title state around the session.

public sealed partial class ProjectController
{
    public EditorSession Session { get; } = new();
    public string? ProjectPath { get; private set; }
    public bool HasProject => Session.Document is not null;

    public event Action? Changed;

    public string DisplayName => ProjectPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(ProjectPath);

    public bool IsModified => Session.IsModified;

    /// A fresh canvas replaces whatever is open (upstream's New Canvas sheet simplified
    /// to width/height for M1; a full sheet lands with the dialog polish pass).
    public void NewCanvas(int width, int height, double resolution = 72)
    {
        Session.CreateDocument(width, height, resolution);
        ProjectPath = null;
        Session.History.MarkSaved();
        Changed?.Invoke();
    }

    public void Open(string path)
    {
        var snapshot = ProjectStore.Load(path);
        Session.LoadSnapshot(snapshot);
        ProjectPath = path;
        Session.History.MarkSaved();
        RecentProjects.Add(path);
        Changed?.Invoke();
    }

    public void Save() => Save(ProjectPath);

    public void Save(string path)
    {
        if (Session.Document is null) return;
        ProjectStore.Save(Session.Document.Snapshot(), path);
        ProjectPath = path;
        Session.History.MarkSaved();
        RecentProjects.Add(path);
        Changed?.Invoke();
    }

    public void ForgetProject()
    {
        ProjectPath = null;
        Changed?.Invoke();
    }

    public void ExportPng(string path)
    {
        if (Session.Document is null) return;
        var canvas = Compositor.Rendering.LayerRenderer.Render(Session.Document);
        File.WriteAllBytes(path, PngCodec.Encode(Session.Document.Width, Session.Document.Height, PngPixelKind.Rgba8, canvas));
    }
}

public static class RecentProjects
{
    private const int Limit = 10;
    public static event Action? Changed;
    public static List<string> Paths { get; private set; } = [];

    public static void Add(string path)
    {
        Paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Paths.Insert(0, path);
        if (Paths.Count > Limit) Paths.RemoveAt(Paths.Count - 1);
        Changed?.Invoke();
    }

    public static void Clear()
    {
        Paths.Clear();
        Changed?.Invoke();
    }
}

