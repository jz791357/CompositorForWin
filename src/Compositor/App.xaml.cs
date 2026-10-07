using System.Windows;

namespace Compositor;

// Maps to upstream CompositorApp.swift + CompositorApplicationDelegate.swift, which
// assemble the menu bar, key commands and the update checker. Those grow here per
// milestone; M0 carries the menu tree and the shell.
public partial class App : Application
{
}
