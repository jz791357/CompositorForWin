using System.IO;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Document;
using Compositor.Editing;
using Compositor.IO;
using WinForms = System.Windows.Forms;

namespace Compositor;

// Upstream ContentView + ProjectController + CompositorApp wiring for M1: menus,
// tools, file dialogs and the session glue around CanvasView and LayersPanel.

public partial class MainWindow : Window
{
    private readonly ProjectController _projects = new();
    internal Controls.CanvasView EditorCanvas { get; }
    private EditorSession Session => _projects.Session;
    private Color _foreground = Colors.Black;

    public MainWindow()
    {
        InitializeComponent();
        // A VM or small screen can be narrower than the 1180×780 default; CenterScreen
        // then leaves the window's sides off-display (the tool header's right end and
        // the Layers panel vanish). Clamp to the work area before anything shows.
        var work = SystemParameters.WorkArea;
        if (Width > work.Width - 16) Width = Math.Max(MinWidth, work.Width - 16);
        if (Height > work.Height - 16) Height = Math.Max(MinHeight, work.Height - 16);
        EditorCanvas = new Controls.CanvasView(Session) { Focusable = true };
        CanvasHost.Child = EditorCanvas;
        LayersHost.Child = new Controls.LayersPanel(Session);
        ToolMove.IsChecked = true;      // parse is done; the Checked handler is safe now
        SourceInitialized += (_, _) => EnableDarkTitleBar();
        Loaded += (_, _) =>
        {
            Session.Changed += OnSessionChanged;
            _projects.Changed += OnProjectChanged;
            RecentProjects.Changed += OnRecentChanged;
            ApplyBrushSettings();
            ForegroundSwatch.Background = new SolidColorBrush(_foreground);
            UpdateStatus();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnSessionChanged() => Dispatcher.Invoke(() =>
    {
        ProjectTab.Header = _projects.DisplayName + (Session.IsModified ? " •" : "");
        UpdateStatus();
    });

    private void OnProjectChanged() => Dispatcher.Invoke(() =>
    {
        ProjectTab.Header = _projects.DisplayName + (_projects.IsModified ? " •" : "");
        if (_projects.HasProject && Session.Document is not null)
            EditorCanvas.FitCanvas();
        UpdateStatus();
    });

    private void OnRecentChanged() => Dispatcher.Invoke(BuildRecentMenu);

    private void UpdateStatus()
    {
        StatusLeft.Text = _projects.HasProject && Session.Document is { } document
            ? $"{document.Width} × {document.Height} px    {document.Layers.Count} layer{(document.Layers.Count == 1 ? "" : "s")}"
            : "";
        StatusRight.Text = $"Zoom {EditorCanvas.Zoom * 100:0}%";
    }

    // --- tools & brush ---

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        Session.Tool = ReferenceEquals(sender, ToolMove) ? Tool.Move
            : ReferenceEquals(sender, ToolBrush) ? Tool.Brush
            : ReferenceEquals(sender, ToolEraser) ? Tool.Eraser
            : Tool.Hand;
    }

    private void BrushSetting_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyBrushSettings();

    private void ApplyBrushSettings()
    {
        // Sliders fire ValueChanged while the XAML is still parsing (setting Minimum
        // coerces Value), so sibling controls may not exist yet — guard those, not
        // the session (whose field initializer always runs).
        if (BrushSize is null || BrushHardness is null || BrushOpacity is null) return;
        Session.Brush = Session.Brush with
        {
            Size = (double)BrushSize.Value,
            Hardness = (double)BrushHardness.Value / 100,
            Opacity = (double)BrushOpacity.Value / 100,
            Red = _foreground.R,
            Green = _foreground.G,
            Blue = _foreground.B,
        };
    }

    private void Foreground_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(_foreground.R, _foreground.G, _foreground.B) };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _foreground = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            ForegroundSwatch.Background = new SolidColorBrush(_foreground);
            ApplyBrushSettings();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var tool = e.Key switch
        {
            Key.V => Tool.Move,
            Key.B => Tool.Brush,
            Key.E => Tool.Eraser,
            Key.H => Tool.Hand,
            _ => (Tool?)null,
        };
        if (tool is { } next)
        {
            Session.Tool = next;
            (next switch
            {
                Tool.Move => ToolMove,
                Tool.Brush => ToolBrush,
                Tool.Eraser => ToolEraser,
                _ => ToolPan,
            }).IsChecked = true;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditorCanvas.SetZoom(1);
            e.Handled = true;
        }
    }

    // --- file commands ---

    private void NewCanvas_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.NewCanvasDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            _projects.NewCanvas(dialog.CanvasWidth, dialog.CanvasHeight, dialog.Resolution);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WinForms.FolderBrowserDialog { Description = "Choose a .comp project folder", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            _projects.Open(dialog.SelectedPath);
        }
        catch (ProjectException error)
        {
            MessageBox.Show(this, error.Message, "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RecentMenu_Opened(object sender, RoutedEventArgs e) => BuildRecentMenu();

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        if (RecentProjects.Paths.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        foreach (var path in RecentProjects.Paths)
        {
            var item = new MenuItem { Header = Path.GetFileNameWithoutExtension(path) };
            var captured = path;
            item.Click += (_, _) =>
            {
                try { _projects.Open(captured); }
                catch (ProjectException error)
                {
                    MessageBox.Show(this, error.Message, "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            RecentMenu.Items.Add(item);
        }
        var clear = new MenuItem { Header = "Clear Menu" };
        clear.Click += (_, _) => RecentProjects.Clear();
        RecentMenu.Items.Add(new Separator());
        RecentMenu.Items.Add(clear);
    }

    private void ImportImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var source = new BitmapImage(new Uri(dialog.FileName));
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var pixels = new byte[width * height * 4];
            var stride = width * 4;
            source.CopyPixels(pixels, stride, 0);
            Session.ImportLayerPixels(Path.GetFileNameWithoutExtension(dialog.FileName), width, height, pixels,
                Math.Min(Session.Document?.Width ?? width, width) == width && (Session.Document?.Height ?? height) >= height
                    ? null : new CGSize?(new CGSize(width, height)));
            EditorCanvas.FitCanvas();
        }
        catch (Exception)
        {
            MessageBox.Show(this, "The image could not be read.", "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) =>
        SaveTo(_projects.ProjectPath ?? AskSavePath());
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveTo(AskSavePath());

    private string? AskSavePath()
    {
        var dialog = new WinForms.FolderBrowserDialog { Description = "Choose where to create the .comp project folder", UseDescriptionForTitle = true };
        return dialog.ShowDialog() == WinForms.DialogResult.OK ? Path.Combine(dialog.SelectedPath, "Untitled.comp") : null;
    }

    private void SaveTo(string? path)
    {
        if (path is null || Session.Document is null) return;
        try
        {
            _projects.Save(path);
        }
        catch (ProjectException error)
        {
            MessageBox.Show(this, error.Message, "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var path = AskExportPath("PNG image|*.png");
        if (path is null) return;
        try { _projects.ExportPng(path); }
        catch (ProjectException error) { MessageBox.Show(this, error.Message, "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ExportJpeg_Click(object sender, RoutedEventArgs e)
    {
        var path = AskExportPath("JPEG image|*.jpg");
        if (path is null || Session.Document is null) return;
        var canvas = Compositor.Rendering.LayerRenderer.Render(Session.Document);
        var bitmap = BitmapSource.Create(Session.Document.Width, Session.Document.Height, 96, 96,
            PixelFormats.Bgra32, null, ConvertBgra(canvas), Session.Document.Width * 4);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static byte[] ConvertBgra(byte[] rgba)
    {
        var bgra = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }
        return bgra;
    }

    private string? AskExportPath(string filter)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void CloseProject_Click(object sender, RoutedEventArgs e)
    {
        if (Session.IsModified && MessageBox.Show(this,
                "Close without saving?", "Compositor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Session.CloseDocument();
        _projects.ForgetProject();
        UpdateStatus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // --- edit / layer / view commands ---

    private void Undo_Click(object sender, RoutedEventArgs e) => Session.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Session.Redo();

    private void FillForeground_Click(object sender, RoutedEventArgs e) =>
        Session.FillActiveLayer(_foreground.R, _foreground.G, _foreground.B);

    private void CanvasSize_Click(object sender, RoutedEventArgs e)
    {
        if (Session.Document is not { } document) return;
        var dialog = new Dialogs.CanvasSizeDialog(document.Width, document.Height) { Owner = this };
        if (dialog.ShowDialog() == true)
            Session.ResizeCanvas(dialog.CanvasWidth, dialog.CanvasHeight);
    }

    private void FlipCanvasH_Click(object sender, RoutedEventArgs e) => Session.FlipCanvas(horizontal: true);
    private void FlipCanvasV_Click(object sender, RoutedEventArgs e) => Session.FlipCanvas(horizontal: false);

    private void NewLayer_Click(object sender, RoutedEventArgs e) => Session.AddBlankLayer();
    private void RenameLayer_Click(object sender, RoutedEventArgs e)
    {
        if (Session.ActiveLayerID is { } id && Session.Document?.ActiveLayer is { } layer)
        {
            var dialog = new Dialogs.InputDialog("Rename Layer", "Layer name:", layer.Name) { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Text))
                Session.RenameLayer(id, dialog.Text);
        }
    }
    private void LayerUp_Click(object sender, RoutedEventArgs e) => Session.MoveActiveLayer(1);
    private void LayerDown_Click(object sender, RoutedEventArgs e) => Session.MoveActiveLayer(-1);
    private void MergeDown_Click(object sender, RoutedEventArgs e) => Session.MergeActiveDown();

    private void FitCanvas_Click(object sender, RoutedEventArgs e) => EditorCanvas.FitCanvas();
    private void ActualPixels_Click(object sender, RoutedEventArgs e) => EditorCanvas.SetZoom(1);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => EditorCanvas.SetZoom(EditorCanvas.Zoom * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => EditorCanvas.SetZoom(EditorCanvas.Zoom / 1.25);

    private void Rulers_Click(object sender, RoutedEventArgs e)
    {
        EditorCanvas.RulersVisible = !EditorCanvas.RulersVisible;
        EditorCanvas.InvalidateVisual();
    }

    private void ClearGuides_Click(object sender, RoutedEventArgs e) => Session.ClearGuides();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            $"Compositor for Windows {typeof(MainWindow).Assembly.GetName().Version}\n\n" +
            "A Windows port of Compositor by Robbie Tilton (robbietilton.com/compositor), MIT licensed.",
            "About Compositor", MessageBoxButton.OK, MessageBoxImage.Information);

    // The macOS original's chrome is uniformly dark; the frame follows on Windows 10 1809+.
    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        foreach (var attribute in new[] { 20, 19 })
        {
            var dark = 1;
            if (DwmSetWindowAttribute(hwnd, attribute, ref dark, sizeof(int)) == 0)
                break;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
