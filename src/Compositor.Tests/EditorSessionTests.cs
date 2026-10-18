using Compositor.Document;
using Compositor.Rendering;
using Xunit;

namespace Compositor.Tests;

// History contracts ported from upstream HistoryTests.swift (the synchronous ones),
// plus blend-math vectors and compositor behavior tests.
public class EditorSessionTests
{
    private static EditorSession SessionWithDocument(int width = 800, int height = 600)
    {
        var session = new EditorSession();
        session.CreateDocument(width, height);
        return session;
    }

    private static string? Fingerprint(CanvasDocument? document) =>
        document is null ? null
        : $"{document.Id}|{document.Width}x{document.Height}|" + string.Join(",",
            document.Layers.Select(l => $"{l.Id}:{l.Name}:{l.IsVisible}:{l.Opacity}"));

    [Fact]
    public void EveryLayerEditRoundTripsWithSelection()
    {
        var session = new EditorSession();
        var states = new List<(string?, Guid?)> { (null, null) };
        void Capture() => states.Add((Fingerprint(session.Document), session.ActiveLayerID));

        session.CreateDocument(800, 600); Capture();
        session.AddBlankLayer(); Capture();
        session.AddBlankLayer(); Capture();
        var id = session.ActiveLayerID!.Value;
        Assert.NotNull(id);
        session.RenameLayer(id, "Foreground"); Capture();
        session.ToggleLayerVisibility(id); Capture();
        session.ReorderLayers(0, 2); Capture();
        session.MoveActiveLayer(1); Capture();
        session.DeleteActiveLayer(); Capture();

        foreach (var expected in Enumerable.Reverse(states.SkipLast(1).ToList()))
        {
            Assert.True(session.CanUndo);
            session.Undo();
            Assert.Equal(expected.Item1, Fingerprint(session.Document));
            Assert.Equal(expected.Item2, session.ActiveLayerID);
        }
        Assert.False(session.CanUndo);
        foreach (var expected in states.Skip(1))
        {
            session.Redo();
            Assert.Equal(expected.Item1, Fingerprint(session.Document));
            Assert.Equal(expected.Item2, session.ActiveLayerID);
        }
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void NoOpEditsLeaveNoHistory()
    {
        var session = SessionWithDocument();
        session.AddBlankLayer();
        var id = session.ActiveLayerID!.Value;
        Assert.NotNull(id);
        session.History.MarkSaved();
        Assert.False(session.IsModified);

        session.RenameLayer(id, "Changed");
        Assert.True(session.IsModified);
        session.Undo();
        Assert.False(session.IsModified);

        var count = session.History.UndoCount;
        session.RenameLayer(id, "   ");        // blank: no-op
        session.ReorderLayers(5, 0);           // out of range: no-op
        Assert.Equal(count, session.History.UndoCount);

        session.Redo();
        Assert.True(session.IsModified);       // redo reapplies the rename
        session.Undo();
        Assert.False(session.IsModified);
        session.AddBlankLayer();
        Assert.False(session.CanRedo);         // a new edit clears redo
        Assert.True(session.IsModified);
    }

    [Fact]
    public void TransactionsGroupAndDocumentReplacementUndoes()
    {
        var session = SessionWithDocument(100, 200);
        session.BeginEdit("Layer Setup");
        session.AddBlankLayer();
        session.AddBlankLayer();
        Assert.False(session.CanUndo);          // pending, not committed
        session.EndEdit();
        Assert.Equal("Layer Setup", session.History.UndoName);
        session.Undo();
        Assert.Empty(session.Document!.Layers);
        session.Redo();

        var previous = session.Document;
        session.CreateDocument(300, 400);
        session.Undo();
        Assert.True(Compositor.Document.DocumentHistory.Equals(previous, session.Document));   // value-equal (undo restores the frozen snapshot)
        session.Redo();
        var replaced = session.Document!;
        Assert.Equal(300, replaced.Width);
        Assert.Equal(400, replaced.Height);
    }

    [Fact]
    public void HistoryBlockedDuringImportsAndDialogs()
    {
        var session = SessionWithDocument(40, 40);
        session.AddBlankLayer();

        session.IsImporting = true;
        session.Undo();
        Assert.NotNull(session.Document);
        session.IsImporting = false;

        session.ShowsNewDocument = true;
        session.Undo();
        Assert.NotNull(session.Document);
        session.ShowsNewDocument = false;

        session.Undo();                          // now free: drops the blank layer
        var empty = session.Document;
        Assert.NotNull(empty);

        session.ShowsImporter = true;
        session.Redo();
        Assert.Equal(empty, session.Document);   // gated: redo did not run
        session.ShowsImporter = false;
        session.Redo();
        Assert.Single(session.Document!.Layers);
    }

    [Fact]
    public void HistoryBoundsEntries()
    {
        // Direct history use: Begin freezes the pre-state, End the post-state, so the
        // edit can mutate the document in place (Swift's value-passing equivalent).
        var history = new DocumentHistory(entryLimit: 2, retainedByteLimit: 0);
        var document = new CanvasDocument { Id = Guid.NewGuid(), Width = 64, Height = 32 };
        document.Layers.Add(ImageLayer.Blank(Guid.NewGuid(), 64, 32));
        var layerID = document.Layers[0].Id;
        for (var i = 0; i < 3; i++)
        {
            history.Begin("Rename", document, layerID);
            document.Layers[0].Name = $"Name {i}";
            history.End(document, layerID);
        }
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(0, history.RetainedBytes(document));   // renames share the pixels
        history.Begin("Delete", document, layerID);
        document.Layers.Clear();
        history.End(document, null);
        Assert.Equal(0, history.UndoCount);                 // the delete retains pixels beyond a 0 budget
        Assert.Equal(0, history.RetainedBytes(document));
    }
}

public class BlendMathTests
{
    [Theory]
    [InlineData(LayerBlendMode.Multiply, 0.5, 0.5, 0.25)]
    [InlineData(LayerBlendMode.Screen, 0.2, 0.3, 0.44)]
    [InlineData(LayerBlendMode.Overlay, 0.4, 0.4, 0.32)]
    [InlineData(LayerBlendMode.Overlay, 0.6, 0.6, 0.68)]
    [InlineData(LayerBlendMode.Difference, 0.8, 0.3, 0.5)]
    [InlineData(LayerBlendMode.LinearBurn, 0.8, 0.5, 0.3)]
    [InlineData(LayerBlendMode.LinearDodge, 0.8, 0.5, 1.0)]
    [InlineData(LayerBlendMode.Subtract, 0.8, 0.3, 0.5)]
    [InlineData(LayerBlendMode.Divide, 0.5, 2.0 / 255.0, null)]   // sentinel: computed below
    public void SeparableVectors(LayerBlendMode mode, double backdrop, double source, double? expected)
    {
        var actual = BlendMath.Channel(mode, backdrop, source);
        if (expected is { } value) Assert.Equal(value, actual, 5);
        else Assert.Equal(Math.Min(1, backdrop / source), actual, 5);
    }

    [Theory]
    [InlineData(LayerBlendMode.ColorBurn, 1.0, 0.4, 1.0)]        // white stays white
    [InlineData(LayerBlendMode.ColorBurn, 0.5, 0.0, 0.0)]        // black source burns to black
    [InlineData(LayerBlendMode.ColorBurn, 0.5, 0.75, 1.0 / 3.0)]
    [InlineData(LayerBlendMode.ColorDodge, 0.0, 0.4, 0.0)]       // black stays black
    [InlineData(LayerBlendMode.ColorDodge, 0.5, 1.0, 1.0)]       // white source dodges to white
    [InlineData(LayerBlendMode.ColorDodge, 0.25, 0.5, 0.5)]
    [InlineData(LayerBlendMode.PinLight, 0.6, 0.3, 0.6)]         // 2s=0.6 → max(0.6, 0.6)
    [InlineData(LayerBlendMode.PinLight, 0.6, 0.2, 0.4)]         // 2s=0.4 → min(0.6, 0.4)
    [InlineData(LayerBlendMode.HardMix, 0.5, 0.9, 1.0)]
    [InlineData(LayerBlendMode.HardMix, 0.4, 0.1, 0.0)]
    public void EdgeCaseVectors(LayerBlendMode mode, double backdrop, double source, double expected) =>
        Assert.Equal(expected, BlendMath.Channel(mode, backdrop, source), 5);

    [Fact]
    public void SoftLightMatchesW3C()
    {
        // b=0.4, s=0.7: b + (2s-1)(sqrt(b)-b) = 0.4 + 0.4*(sqrt(0.4)-0.4)
        var expected = 0.4 + 0.4 * (Math.Sqrt(0.4) - 0.4);
        Assert.Equal(expected, BlendMath.Channel(LayerBlendMode.SoftLight, 0.4, 0.7), 6);
        // b=0.7, s=0.2: b - (1-2s)·b·(1-b)
        var dark = 0.7 - 0.6 * 0.7 * 0.3;
        Assert.Equal(dark, BlendMath.Channel(LayerBlendMode.SoftLight, 0.7, 0.2), 6);
    }

    [Fact]
    public void HueOverAchromaticBackdropKeepsBackdropGray()
    {
        var result = BlendMath.NonSeparable(LayerBlendMode.Hue, (0.5, 0.5, 0.5), (1.0, 0.0, 0.0));
        Assert.Equal(0.5, result.R, 4);
        Assert.Equal(0.5, result.G, 4);
        Assert.Equal(0.5, result.B, 4);
    }

    [Fact]
    public void LuminosityTakesTheSourceLightness()
    {
        var result = BlendMath.NonSeparable(LayerBlendMode.Luminosity, (1.0, 0.0, 0.0), (1.0, 1.0, 1.0));
        Assert.Equal(1.0, result.R, 4);
        Assert.Equal(1.0, result.G, 4);
        Assert.Equal(1.0, result.B, 4);
    }

    [Fact]
    public void SaturationOverGrayStaysGray()
    {
        var result = BlendMath.NonSeparable(LayerBlendMode.Saturation, (0.5, 0.5, 0.5), (1.0, 0.5, 0.0));
        Assert.Equal(0.5, result.R, 4);
        Assert.Equal(0.5, result.G, 4);
        Assert.Equal(0.5, result.B, 4);
    }
}

public class CompositorTests
{
    private static ImageLayer SolidLayer(Guid id, int width, int height, byte r, byte g, byte b, byte a = 255,
        LayerBlendMode blend = LayerBlendMode.Normal, double opacity = 1)
    {
        var layer = ImageLayer.Blank(id, width, height);
        var pixels = layer.Pixels!;
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 4] = r; pixels[i * 4 + 1] = g; pixels[i * 4 + 2] = b; pixels[i * 4 + 3] = a;
        }
        layer.BlendMode = blend;
        layer.Opacity = opacity;
        return layer;
    }

    private static CanvasDocument Document(int width, int height, params ImageLayer[] layers)
    {
        var document = new CanvasDocument { Id = Guid.NewGuid(), Width = width, Height = height };
        document.Layers.AddRange(layers);
        return document;
    }

    [Fact]
    public void OpaqueTopLayerCoversTheOneBelow()
    {
        var document = Document(4, 4,
            SolidLayer(Guid.NewGuid(), 4, 4, 255, 0, 0),
            SolidLayer(Guid.NewGuid(), 4, 4, 0, 0, 255));
        var canvas = LayerRenderer.Render(document);
        Assert.Equal(0, canvas[0]);        // opaque blue wins: its R is 0...
        Assert.Equal(255, canvas[2]);      // ...and its B is 255
        Assert.Equal(255, canvas[3]);
    }

    [Fact]
    public void HalfOpacityOverWhiteIsMidGray()
    {
        var document = Document(2, 2,
            SolidLayer(Guid.NewGuid(), 2, 2, 255, 255, 255),
            SolidLayer(Guid.NewGuid(), 2, 2, 0, 0, 0, opacity: 0.5));
        var canvas = LayerRenderer.Render(document);
        Assert.Equal(128, canvas[0]);       // 255 * 0.5
        Assert.Equal(255, canvas[3]);
    }

    [Fact]
    public void MultiplyDarkens()
    {
        var document = Document(2, 2,
            SolidLayer(Guid.NewGuid(), 2, 2, 255, 255, 255),
            SolidLayer(Guid.NewGuid(), 2, 2, 128, 128, 128, blend: LayerBlendMode.Multiply));
        var canvas = LayerRenderer.Render(document);
        Assert.Equal(128, canvas[0]);
    }

    [Fact]
    public void FolderOpacityDimsEverythingInside()
    {
        var folder = new ImageLayer
        {
            Id = Guid.NewGuid(), Name = "Folder", IsGroup = true, Opacity = 0.5,
            Transform = new LayerTransform(new CGPoint(0, 0), new CGSize(4, 4)),
            Width = 4, Height = 4,
        };
        var inner = SolidLayer(Guid.NewGuid(), 4, 4, 255, 255, 255);
        inner.ParentID = folder.Id;
        var document = Document(4, 4, folder, inner);

        var canvas = LayerRenderer.Render(document);
        Assert.Equal(255, canvas[0]);       // the color stays white...
        Assert.Equal(128, canvas[3]);       // ...at half coverage from the folder
    }

    [Fact]
    public void HiddenLayerDoesNotDraw()
    {
        var layer = SolidLayer(Guid.NewGuid(), 2, 2, 255, 0, 0);
        layer.IsVisible = false;
        var canvas = LayerRenderer.Render(Document(2, 2, layer));
        Assert.Equal(0, canvas[3]);
    }

    [Fact]
    public void RotationNinetySwapsTheLayerAxes()
    {
        // A 2x1 solid layer rotated 90° clockwise covers a 1x2 area instead.
        var layer = SolidLayer(Guid.NewGuid(), 2, 1, 255, 0, 0);
        layer.Transform = new LayerTransform(new CGPoint(1, 1), new CGSize(2, 1)) { Rotation = 90 };
        var canvas = LayerRenderer.Render(Document(4, 4, layer));
        Assert.Equal(255, canvas[(1 * 4 + 2) * 4]);     // (2,1): inside the rotated box
        Assert.Equal(0, canvas[(1 * 4 + 0) * 4 + 3]);   // (0,1): outside it
        Assert.Equal(0, canvas[(3 * 4 + 1) * 4 + 3]);   // (1,3): outside it
    }

    [Fact]
    public void BlackMaskHidesTheLayer()
    {
        var layer = SolidLayer(Guid.NewGuid(), 2, 2, 255, 0, 0);
        layer.Mask = new LayerMaskBuffer { Width = 2, Height = 2, Gray = [0, 0, 0, 0] };
        var canvas = LayerRenderer.Render(Document(2, 2, layer));
        Assert.Equal(0, canvas[3]);
    }

    [Fact]
    public void PartialMaskHalvesTheCoverage()
    {
        var layer = SolidLayer(Guid.NewGuid(), 2, 2, 255, 255, 255);
        layer.Mask = new LayerMaskBuffer { Width = 2, Height = 2, Gray = [255, 255, 255, 255], Enabled = true };
        layer.Mask = new LayerMaskBuffer { Width = 1, Height = 1, Gray = [128] };   // uniform half
        var canvas = LayerRenderer.Render(Document(2, 2, layer));
        Assert.Equal(128, canvas[3]);       // alpha ≈ 0.5
    }

    [Fact]
    public void DownscaledLayerSamplesNearestExactly()
    {
        var layer = SolidLayer(Guid.NewGuid(), 2, 2, 255, 0, 0);
        layer.Transform = new LayerTransform(new CGPoint(0, 0), new CGSize(1, 1)) { Sampling = LayerSampling.Nearest };
        var canvas = LayerRenderer.Render(Document(2, 2, layer));
        Assert.Equal(255, canvas[0]);       // only the first pixel covered
        Assert.Equal(0, canvas[(1 * 2 + 1) * 4 + 3]);
    }
}
