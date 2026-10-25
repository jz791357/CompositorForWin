using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Compositor.Document;
using Compositor.Editing;

namespace Compositor.Controls;

// The Layers panel (upstream LayersPanel + NativeLayerList, M1 shape): top-to-bottom
// listing of the layer tree (topmost first), inline rename, visibility toggles,
// opacity and blend for the active layer, mask management.

public sealed class LayersPanel : UserControl
{
    private readonly EditorSession _session;
    private readonly DockPanel _root = new();
    private readonly StackPanel _list = new();
    private readonly Slider _opacity = new();
    private readonly ComboBox _blend = new();
    private Guid _renaming;
    private TextBox? _renameBox;

    public LayersPanel(EditorSession session)
    {
        _session = session;
        Build();
        _session.Changed += () => Dispatcher.Invoke(Rebuild);
        Rebuild();
    }

    private void Build()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        Content = _root;
        var header = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
        var title = new TextBlock { Text = "Layers", FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)) };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, tooltip, action) in new (string, string, Action)[]
        {
            ("＋", "New blank layer", () => _session.AddBlankLayer()),
            ("🗑", "Delete layer", () => _session.DeleteActiveLayer()),
            ("▲", "Move layer up", () => _session.MoveActiveLayer(1)),
            ("▼", "Move layer down", () => _session.MoveActiveLayer(-1)),
            ("⊞", "Group into a folder", () => _session.GroupSelectedLayers()),
        })
        {
            var button = new Button { Content = label, ToolTip = tooltip, Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(6, 2, 6, 2), MinWidth = 28 };
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        DockPanel.SetDock(header, Dock.Top);
        _root.Children.Add(header);

        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _list.Orientation = Orientation.Vertical;
        scroller.Content = _list;
        _root.Children.Add(scroller);

        var footer = new StackPanel { Margin = new Thickness(12, 8, 12, 10) };
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal };
        opacityRow.Children.Add(new TextBlock { Text = "Opacity  ", Foreground = Brush("B0B0B0"), VerticalAlignment = VerticalAlignment.Center });
        _opacity.Minimum = 0; _opacity.Maximum = 100; _opacity.Width = 110;
        _opacity.ValueChanged += (_, _) =>
        {
            if (_session.Document?.ActiveLayer is { } layer && Math.Abs(layer.Opacity * 100 - _opacity.Value) > 0.5)
                _session.SetLayerOpacity(layer.Id, _opacity.Value / 100);
        };
        opacityRow.Children.Add(_opacity);
        footer.Children.Add(opacityRow);

        var blendRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        blendRow.Children.Add(new TextBlock { Text = "Blend  ", Foreground = Brush("B0B0B0"), VerticalAlignment = VerticalAlignment.Center });
        foreach (var group in LayerBlendModes.Groups)
        {
            foreach (var mode in group) _blend.Items.Add(mode.ToString());
            if (group != LayerBlendModes.Groups[^1]) _blend.Items.Add(new Separator());
        }
        _blend.SelectionChanged += (_, _) =>
        {
            if (_session.Document?.ActiveLayer is { } layer && _blend.SelectedItem is string name
                && Enum.TryParse<LayerBlendMode>(name, out var mode))
                _session.SetLayerBlendMode(layer.Id, mode);
        };
        _blend.Width = 130;
        blendRow.Children.Add(_blend);
        footer.Children.Add(blendRow);

        var maskRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, tooltip, action) in new (string, string, Action)[]
        {
            ("Add Mask", "A white mask revealing the layer", () => _session.AddRevealingMask()),
            ("Remove Mask", "Drop the layer's mask", () => _session.RemoveMask()),
            ("Toggle Mask", "Enable or disable the mask", () => _session.ToggleMaskEnabled()),
            ("Paint Mask", "Paint the mask instead of the pixels (white; Alt-click paints black)", ToggleMaskTarget),
        })
        {
            var button = new Button { Content = label, ToolTip = tooltip, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 3, 8, 3) };
            button.Click += (_, _) => action();
            maskRow.Children.Add(button);
        }
        footer.Children.Add(maskRow);
        DockPanel.SetDock(footer, Dock.Bottom);
        // Short windows: keep the footer reachable instead of clipped away.
        var footerScroll = new ScrollViewer
        {
            Content = footer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        DockPanel.SetDock(footerScroll, Dock.Bottom);
        _root.Children.Add(footerScroll);
    }

    private void ToggleMaskTarget()
    {
        _session.SetStrokeTarget(_session.StrokeTargetValue == Document.StrokeTarget.LayerPixels
            ? Document.StrokeTarget.LayerMask
            : Document.StrokeTarget.LayerPixels);
    }

    private static Brush Brush(string hex) => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString("#" + hex));

    private void Rebuild()
    {
        _list.Children.Clear();
        _renameBox = null;
        var document = _session.Document;
        if (document is null) return;

        // Topmost layer first, folders expandable in place (M1: always expanded).
        foreach (var entry in LayerHierarchy.Entries(document.Layers, topFirst: true))
        {
            var layer = entry.Layer;
            var row = new DockPanel { Margin = new Thickness(12, 2, 12, 2), Tag = layer.Id };
            row.MouseLeftButtonUp += (_, _) => _session.SelectLayer(layer.Id);

            var eye = new Button { Content = layer.IsVisible ? "👁" : "·", MinWidth = 26, Padding = new Thickness(2),
                ToolTip = "Toggle visibility" };
            eye.Click += (_, _) => _session.ToggleLayerVisibility(layer.Id);
            DockPanel.SetDock(eye, Dock.Left);
            row.Children.Add(eye);

            var maskBadge = new TextBlock { Text = layer.Mask is null ? "" : "◐",
                Foreground = Brush("4A90E2"), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = layer.Mask is null ? null : layer.Mask.Enabled ? "Mask enabled" : "Mask disabled" };
            DockPanel.SetDock(maskBadge, Dock.Right);
            row.Children.Add(maskBadge);

            FrameworkElement name;
            if (_renaming == layer.Id)
            {
                _renameBox = new TextBox { Text = layer.Name };
                _renameBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) FinishRename();
                    if (e.Key == Key.Escape) { _renaming = Guid.Empty; Rebuild(); }
                };
                _renameBox.LostFocus += (_, _) => FinishRename();
                name = _renameBox;
            }
            else
            {
                var label = new TextBlock { Text = layer.Name, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("E8E8E8"), Margin = new Thickness(8, 3, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                label.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2) { _renaming = layer.Id; Rebuild(); _renameBox?.Focus(); _renameBox?.SelectAll(); }
                };
                name = label;
            }
            row.Children.Add(name);

            row.Background = layer.Id == document.ActiveLayerID
                ? new SolidColorBrush(Color.FromArgb(70, 0x4A, 0x90, 0xE2))
                : Brushes.Transparent;
            row.ContextMenu = LayerMenu(layer.Id);
            _list.Children.Add(row);
        }

        var active = document.ActiveLayer;
        _opacity.Value = (active?.Opacity ?? 1) * 100;
        var current = active?.BlendMode.ToString();
        if (_blend.SelectedItem as string != current && current is not null)
            _blend.SelectedItem = current;
    }

    private ContextMenu LayerMenu(Guid id)
    {
        var menu = new ContextMenu();
        void Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Rename…", () => { _renaming = id; Rebuild(); _renameBox?.Focus(); _renameBox?.SelectAll(); });
        Item("Duplicate Layer", () => _session.DuplicateLayer(id));
        Item("New Blank Layer Above", _session.AddBlankLayer);
        Item("Delete Layer", _session.DeleteActiveLayer);
        menu.Items.Add(new Separator());
        Item("Group Selected Layers", _session.GroupSelectedLayers);
        Item("Ungroup Layers", _session.UngroupLayers);
        return menu;
    }

    private void FinishRename()
    {
        if (_renameBox is null) return;
        var id = _renaming;
        var text = _renameBox.Text;
        _renaming = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(text)) _session.RenameLayer(id, text);
        Rebuild();
    }
}
