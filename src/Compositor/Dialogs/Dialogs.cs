using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Compositor.Dialogs;

// Small dark dialogs for M1 (upstream NewCanvasSheet / CanvasSizeSheet get their full
// polish pass later; these carry the same fields).

public sealed class NewCanvasDialog : Window
{
    public int CanvasWidth { get; private set; } = 1920;
    public int CanvasHeight { get; private set; } = 1080;
    public double Resolution { get; private set; } = 72;

    public NewCanvasDialog()
    {
        Title = "New Canvas";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));

        var panel = new StackPanel { Margin = new Thickness(20) };
        TextBox Make(string label, string text)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
            var box = new TextBox { Text = text };
            panel.Children.Add(box);
            return box;
        }
        var width = Make("Width (pixels)", "1920");
        var height = Make("Height (pixels)", "1080");
        var resolution = Make("Resolution (pixels/inch)", "72");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        void Add(string content, RoutedEventHandler handler, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = content, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            button.Click += handler;
            if (isDefault) button.IsDefault = true;
            if (isCancel) button.IsCancel = true;
            buttons.Children.Add(button);
        }
        Add("Cancel", (_, _) => DialogResult = false, isCancel: true);
        Add("Create", (_, _) =>
        {
            if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h)
                || w < 1 || h < 1 || w > 30000 || h > 30000
                || !double.TryParse(resolution.Text, out var dpi) || dpi < 1 || dpi > 9600)
            {
                MessageBox.Show(this, "Enter width and height between 1 and 30000 pixels, resolution 1–9600.",
                    "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CanvasWidth = w; CanvasHeight = h; Resolution = dpi;
            DialogResult = true;
        }, isDefault: true);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

public sealed class CanvasSizeDialog : Window
{
    public int CanvasWidth { get; private set; }
    public int CanvasHeight { get; private set; }

    public CanvasSizeDialog(int currentWidth, int currentHeight)
    {
        Title = "Canvas Size";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"Current: {currentWidth} × {currentHeight}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
        TextBox Make(string label, string text)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
            var box = new TextBox { Text = text };
            panel.Children.Add(box);
            return box;
        }
        var width = Make("New width", currentWidth.ToString());
        var height = Make("New height", currentHeight.ToString());

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        var apply = new Button { Content = "Apply", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(width.Text, out var w) || !int.TryParse(height.Text, out var h)
                || w < 1 || h < 1 || w > 30000 || h > 30000)
            {
                MessageBox.Show(this, "Enter width and height between 1 and 30000 pixels.",
                    "Compositor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CanvasWidth = w; CanvasHeight = h;
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

public sealed class InputDialog : Window
{
    public string Text => box.Text;
    private readonly TextBox box;

    public InputDialog(string title, string label, string initial)
    {
        Title = title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)) });
        box = new TextBox { Text = initial, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        var ok = new Button { Content = "OK", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}
