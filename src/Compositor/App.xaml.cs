using System.IO;
using System.Windows;

namespace Compositor;

// Maps to upstream CompositorApp.swift + CompositorApplicationDelegate.swift, which
// assemble the menu bar, key commands and the update checker. M1 carries startup
// plus crash reporting: anything that stops the app from opening writes
// %LOCALAPPDATA%\Compositor\startup.log so VM/driver problems can be diagnosed.

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.LocalApplicationData), "Compositor", "startup.log");

    public App()
    {
        // WPF needs DirectX here; VMs without working 3D acceleration can fail to open
        // any window. Set COMPOSITOR_SOFTWARE_RENDER=1 to force the software rasterizer.
        if (Environment.GetEnvironmentVariable("COMPOSITOR_SOFTWARE_RENDER") == "1")
            AppContext.SetSwitch("Switch.System.Windows.Media.DisableHWAcceleration", true);
        Log("starting");
        DispatcherUnhandledException += (_, e) =>
        {
            Log($"UI thread: {e.Exception}");
            MessageBox.Show(e.Exception.ToString(), "Compositor", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log($"unhandled: {e.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log($"unobserved task: {e.Exception}");
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("startup");
        base.OnStartup(e);
        Log("startup complete");
    }

    internal static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be the thing that keeps the app from starting.
        }
    }
}
