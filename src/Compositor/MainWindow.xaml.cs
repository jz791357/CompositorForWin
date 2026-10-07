using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Compositor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => EnableDarkTitleBar();
    }

    // The macOS original's chrome is uniformly dark; the frame follows on Windows 10 1809+.
    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        foreach (var attribute in new[] { 20, 19 }) // DWMWA_USE_IMMERSIVE_DARK_MODE, then its pre-1903 spelling
        {
            if (DwmSetWindowAttribute(hwnd, attribute, ref _dark, sizeof(int)) == 0)
                break;
        }
    }
    private int _dark = 1;

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "Automatic updates arrive with the M4 release channel (Velopack).",
            "Compositor", MessageBoxButton.OK, MessageBoxImage.Information);

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            $"Compositor for Windows {typeof(MainWindow).Assembly.GetName().Version}\n\n" +
            "A Windows port of Compositor by Robbie Tilton (robbietilton.com/compositor), MIT licensed.",
            "About Compositor", MessageBoxButton.OK, MessageBoxImage.Information);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
