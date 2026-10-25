using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Document;
using Compositor.Editing;

namespace Compositor.Controls;

// The canvas viewport (upstream EditorCanvas + CanvasViewport + GPUCanvas, M1 shape):
// scroll-wheel zoom anchored at the cursor, space/middle-drag pan, tool input routed
// to the session (paint strokes, layer drags), rulers with draggable guides, and a
// two-tier render — a cached composite of everything below the active layer plus the
// active layer's own bitmap — so brush strokes only rewrite one layer.

public sealed class CanvasView : FrameworkElement
{
    private readonly EditorSession _session;
    private byte[]? _basePixels;        // layers below the active one, composited (straight RGBA)
    private byte[]? _activePixels;      // the active layer alone (straight RGBA)
    private WriteableBitmap? _composite;      // base + active, canvas-sized

    private double _zoom = 1;
    private Vector _offset = new(80, 80);
    private CGPoint? _lastDocumentPoint;
    private bool _panning;
    private Point _panStart;
    private Vector _panStartOffset;
    private bool _draggingLayer;
    private Point _dragStart;
    private readonly HashSet<CanvasGuide> _draggedGuides = [];

    private const double RulerSize = 20;

    public bool RulersVisible { get; set; } = true;

    public CanvasView(EditorSession session)
    {
        _session = session;
        _session.Changed += () => Dispatcher.Invoke(() =>
        {
            RebuildAll();
            InvalidateVisual();
        });
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        Loaded += (_, _) => Keyboard.Focus(this);
    }

    public double Zoom => _zoom;

    public void SetZoom(double zoom, Point? anchor = null)
    {
        var anchorPoint = anchor ?? new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        var document = ScreenToDocument(anchorPoint);
        _zoom = Math.Clamp(zoom, 0.02, 32);
        _offset = (Vector)DocumentToScreen(document);
        InvalidateVisual();
    }

    public void FitCanvas()
    {
        if (_session.Document is not { } document) return;
        var width = Math.Max(1, RenderSize.Width - _offset.X - 40);
        var height = Math.Max(1, RenderSize.Height - _offset.Y - 40);
        _zoom = Math.Clamp(Math.Min(width / document.Width, height / document.Height), 0.02, 32);
        _offset = new Vector(
            (RenderSize.Width - document.Width * _zoom) / 2,
            (RenderSize.Height - document.Height * _zoom) / 2);
        InvalidateVisual();
    }

    // --- coordinate mapping (screen includes the ruler strip) ---

    private Point DocumentToScreen(CGPoint document) =>
        new(_offset.X + document.X * _zoom, _offset.Y + document.Y * _zoom);

    private CGPoint ScreenToDocument(Point screen) =>
        new((screen.X - _offset.X) / _zoom, (screen.Y - _offset.Y) / _zoom);

    // --- rendering ---

    private void RebuildAll()
    {
        if (_session.Document is not { } document)
        {
            _basePixels = _activePixels = null;
            _composite = null;
            return;
        }
        var width = document.Width;
        var height = document.Height;
        _activePixels = null;
        foreach (var layer in document.Layers)
        {
            if (layer.Pixels is null) continue;
            if (layer.Id == document.ActiveLayerID)
            {
                _activePixels = RasterizeLayer(layer, width, height);
                break;
            }
        }
        _basePixels = RenderBase(document);
        _composite = Combine(_basePixels, _activePixels, width, height);
    }

    private static byte[]? RenderBase(CanvasDocument document)
    {
        // Composite everything except the active pixel layer.
        var layers = document.Layers;
        var activeIndex = layers.FindIndex(l => l.Id == document.ActiveLayerID);
        var copy = new CanvasDocument
        {
            Id = document.Id, Width = document.Width, Height = document.Height,
            Guides = document.Guides, ActiveLayerID = null,
            Layers = layers.Where((_, index) => index != activeIndex).ToList(),
        };
        return Compositor.Rendering.LayerRenderer.Render(copy);
    }

    private static byte[]? RasterizeLayer(ImageLayer layer, int width, int height)
    {
        var copy = new CanvasDocument
        {
            Id = Guid.NewGuid(), Width = width, Height = height,
            Layers = [CanvasDocument.LayerCopy(layer)],
        };
        return Compositor.Rendering.LayerRenderer.Render(copy);
    }

    private static WriteableBitmap? Combine(byte[]? basis, byte[]? top, int width, int height)
    {
        if (top is null) return basis is null ? null : ToBitmap(basis, width, height);
        if (basis is null) return ToBitmap(top, width, height);
        var blended = new byte[basis.Length];
        for (var i = 0; i < width * height; i++)
        {
            var sourceA = top[i * 4 + 3] / 255.0;
            var backdropA = basis[i * 4 + 3] / 255.0;
            var outA = sourceA + backdropA * (1 - sourceA);
            if (outA <= 0) continue;
            blended[i * 4] = (byte)Math.Round((top[i * 4] * sourceA + basis[i * 4] * backdropA * (1 - sourceA)) / outA);
            blended[i * 4 + 1] = (byte)Math.Round((top[i * 4 + 1] * sourceA + basis[i * 4 + 1] * backdropA * (1 - sourceA)) / outA);
            blended[i * 4 + 2] = (byte)Math.Round((top[i * 4 + 2] * sourceA + basis[i * 4 + 2] * backdropA * (1 - sourceA)) / outA);
            blended[i * 4 + 3] = (byte)Math.Round(outA * 255);
        }
        return ToBitmap(blended, width, height);
    }

    private static WriteableBitmap ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        var converted = new byte[pixels.Length];
        // Straight RGBA → PBGRA for WPF.
        for (var i = 0; i < width * height; i++)
        {
            var a = pixels[i * 4 + 3] / 255.0;
            converted[i * 4] = (byte)Math.Round(pixels[i * 4] * a);
            converted[i * 4 + 1] = (byte)Math.Round(pixels[i * 4 + 1] * a);
            converted[i * 4 + 2] = (byte)Math.Round(pixels[i * 4 + 2] * a);
            converted[i * 4 + 3] = pixels[i * 4 + 3];
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), converted, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var background = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), null, background);

        if (_session.Document is not { } document)
        {
            DrawNoDocumentHint(context);
            return;
        }

        var ruler = RulersVisible ? RulerSize : 0;
        context.PushClip(new RectangleGeometry(new Rect(ruler, ruler,
            Math.Max(0, RenderSize.Width - ruler), Math.Max(0, RenderSize.Height - ruler))));
        var destination = new Rect(DocumentToScreen(new CGPoint(0, 0)),
            new Size(document.Width * _zoom, document.Height * _zoom));
        DrawCheckerboard(context, destination);
        if (_composite is not null)
        {
            context.DrawImage(_composite, destination);
            if (_zoom >= 8) DrawPixelGrid(context, destination);
        }
        DrawGuides(context);
        context.Pop();

        if (RulersVisible) DrawRulers(context);
    }

    private void DrawNoDocumentHint(DrawingContext context)
    {
        var text = new FormattedText("File > New Canvas… to start",
            System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 14, new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)), 1);
        context.DrawText(text, new Point((RenderSize.Width - text.Width) / 2, (RenderSize.Height - text.Height) / 2));
    }

    // The transparency checkerboard as a cached 16×16 tile brush — drawing thousands of
    // little rectangles per frame made guide drags and pans stutter, especially under a
    // VM's software renderer.
    private static ImageBrush? _checkerTile;

    private static void DrawCheckerboard(DrawingContext context, Rect destination)
    {
        if (_checkerTile is null)
        {
            var tile = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgr32, null);
            var pixels = new uint[16 * 16];
            uint light = 0xFF484848, dark = 0xFF3C3C3C;   // BGR little-endian
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    pixels[y * 16 + x] = (x / 8 + y / 8) % 2 == 0 ? light : dark;
            tile.WritePixels(new Int32Rect(0, 0, 16, 16), pixels, 16 * 4, 0);
            tile.Freeze();
            _checkerTile = new ImageBrush(tile)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            _checkerTile.Freeze();
        }
        // Keep the pattern aligned with the canvas origin while it pans or zooms.
        // (Brushes can't be re-frozen, but a value-copy with the frame's transform is
        // cheap compared to drawing the tiles by hand.)
        var aligned = _checkerTile.CloneCurrentValue();
        aligned.Transform = new TranslateTransform(destination.X % 16, destination.Y % 16);
        context.DrawRectangle(aligned, null, destination);
    }

    private void DrawPixelGrid(DrawingContext context, Rect destination)
    {
        // Thousands of individual lines stutter in software rendering; the grid only
        // matters close up, where few lines are on screen at once.
        var visibleX = RenderSize.Width / _zoom;
        var visibleY = RenderSize.Height / _zoom;
        if (visibleX + visibleY > 1500) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1);
        pen.Freeze();
        for (var x = 0; x <= _session.Document!.Width; x++)
        {
            var screen = destination.X + x * _zoom;
            if (screen < 0 || screen > RenderSize.Width) continue;
            context.DrawLine(pen, new Point(screen, Math.Max(destination.Y, 0)),
                new Point(screen, Math.Min(destination.Bottom, RenderSize.Height)));
        }
        for (var y = 0; y <= _session.Document.Height; y++)
        {
            var screen = destination.Y + y * _zoom;
            if (screen < 0 || screen > RenderSize.Height) continue;
            context.DrawLine(pen, new Point(Math.Max(destination.X, 0), screen),
                new Point(Math.Min(destination.Right, RenderSize.Width), screen));
        }
    }

    private void DrawGuides(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)), 1);
        pen.Freeze();
        foreach (var guide in _session.Document?.Guides ?? [])
        {
            if (guide.Axis == CanvasGuideAxis.Vertical)
            {
                var x = DocumentToScreen(new CGPoint(guide.Position, 0)).X;
                context.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
            }
            else
            {
                var y = DocumentToScreen(new CGPoint(0, guide.Position)).Y;
                context.DrawLine(pen, new Point(0, y), new Point(RenderSize.Width, y));
            }
        }
    }

    private void DrawRulers(DrawingContext context)
    {
        var chrome = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        var text = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        context.DrawRectangle(chrome, null, new Rect(0, 0, RenderSize.Width, RulerSize));
        context.DrawRectangle(chrome, null, new Rect(0, 0, RulerSize, RenderSize.Height));
        var typeface = new Typeface("Segoe UI");
        double Step() => _zoom >= 8 ? 1 : _zoom >= 2 ? 10 : _zoom >= 0.5 ? 50 : _zoom >= 0.1 ? 250 : 1000;
        var step = Step();
        var pen = new Pen(text, 1);
        for (var x = Math.Floor(-_offset.X / _zoom / step) * step; ; x += step)
        {
            var screen = _offset.X + x * _zoom;
            if (screen > RenderSize.Width) break;
            if (screen >= RulerSize)
            {
                context.DrawLine(pen, new Point(screen, RulerSize - 6), new Point(screen, RulerSize));
                var label = new FormattedText($"{x:0}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 9, text, 1);
                if (label.Width < step * _zoom - 2)
                    context.DrawText(label, new Point(screen + 2, 2));
            }
        }
        for (var y = Math.Floor(-_offset.Y / _zoom / step) * step; ; y += step)
        {
            var screen = _offset.Y + y * _zoom;
            if (screen > RenderSize.Height) break;
            if (screen >= RulerSize)
            {
                context.DrawLine(pen, new Point(RulerSize - 6, screen), new Point(RulerSize, screen));
                var label = new FormattedText($"{y:0}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 9, text, 1);
                if (label.Width < step * _zoom - 2)
                    context.DrawText(label, new Point(2, screen + 1));
            }
        }
    }

    // --- input ---

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        SetZoom(_zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        Keyboard.Focus(this);
        var position = e.GetPosition(this);
        // Drag a new guide out of a ruler.
        if (RulersVisible && _session.Document is not null)
        {
            if (position.Y < RulerSize && position.X >= RulerSize && e.ChangedButton == MouseButton.Left)
            {
                var guide = new CanvasGuide(Guid.NewGuid(), CanvasGuideAxis.Horizontal, 0);
                BeginGuideDrag(guide);
                e.Handled = true;
                return;
            }
            if (position.X < RulerSize && position.Y >= RulerSize && e.ChangedButton == MouseButton.Left)
            {
                var guide = new CanvasGuide(Guid.NewGuid(), CanvasGuideAxis.Vertical, 0);
                BeginGuideDrag(guide);
                e.Handled = true;
                return;
            }
        }
        if (e.ChangedButton == MouseButton.Middle || Keyboard.IsKeyDown(Key.Space))
        {
            _panning = true;
            _panStart = position;
            _panStartOffset = _offset;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left && _session.Document is not null)
        {
            var document = ScreenToDocument(position);
            if (_session.Tool == Tool.Move)
            {
                _draggingLayer = _session.BeginLayerDrag(document);
                _dragStart = position;
                if (_draggingLayer) CaptureMouse();
            }
            else if (_session.Tool is Tool.Brush or Tool.Eraser)
            {
                _lastDocumentPoint = document;
                _session.BeginStroke();
                _session.ExtendStroke(document, document);
                CaptureMouse();
            }
        }
        e.Handled = true;
    }

    private void BeginGuideDrag(CanvasGuide guide)
    {
        _session.Document!.Guides.Add(guide);
        _draggedGuides.Add(guide);
        _session.BeginEdit("Add Guide");
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (_panning)
        {
            _offset = _panStartOffset + (position - _panStart);
            InvalidateVisual();
            return;
        }
        if (_draggedGuides.Count > 0 && _session.Document is not null)
        {
            var document = ScreenToDocument(position);
            foreach (var guide in _session.Document.Guides.Where(g => _draggedGuides.Contains(g)).ToList())
            {
                var index = _session.Document.Guides.IndexOf(guide);
                _session.Document.Guides[index] = guide with { Position = guide.Axis == CanvasGuideAxis.Vertical ? document.X : document.Y };
            }
            InvalidateVisual();
            return;
        }
        if (_draggingLayer)
        {
            var delta = (position - _dragStart) / _zoom;
            _session.DragLayerBy(Math.Round(delta.X), Math.Round(delta.Y));
            return;
        }
        if (_lastDocumentPoint is { } previous && e.LeftButton == MouseButtonState.Pressed)
        {
            var document = ScreenToDocument(position);
            _session.ExtendStroke(previous, document);
            _lastDocumentPoint = document;
        }
        // No cursor ring is drawn yet, so a plain hover needs no repaint.
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_draggedGuides.Count > 0)
        {
            var position = e.GetPosition(this);
            // Released outside the canvas: the guide vanishes.
            var document = ScreenToDocument(position);
            var inCanvas = document.X >= 0 && document.Y >= 0
                && document.X <= _session.Document!.Width && document.Y <= _session.Document.Height;
            if (!inCanvas)
                _session.Document!.Guides.RemoveAll(g => _draggedGuides.Contains(g));
            _draggedGuides.Clear();
            _session.EndEdit();
        }
        if (_draggingLayer)
        {
            _draggingLayer = false;
            _session.EndLayerDrag();
        }
        if (_lastDocumentPoint is not null)
        {
            _session.EndStroke();
            _lastDocumentPoint = null;
        }
        _panning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }
}
