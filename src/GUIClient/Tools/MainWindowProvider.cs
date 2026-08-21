using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace GUIClient.Tools;

public class MainWindowProvider: IMainWindowProvider
{
    public Avalonia.Controls.Window GetMainWindow() => Lifetime.MainWindow!;

    public Avalonia.Controls.Window GetActiveWindow()
    {
        var lifetime = Lifetime;

        // Windows is ordered oldest-first; an active child window is the launcher.
        var active = lifetime.Windows.LastOrDefault(w => w.IsActive && w.IsVisible);

        return active ?? lifetime.MainWindow!;
    }

    private static IClassicDesktopStyleApplicationLifetime Lifetime =>
        (IClassicDesktopStyleApplicationLifetime) Application.Current?.ApplicationLifetime!;
}
