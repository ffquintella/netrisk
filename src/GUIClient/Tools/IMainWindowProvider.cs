using Avalonia.Controls;

namespace GUIClient.Tools;

public interface IMainWindowProvider
{
    Avalonia.Controls.Window GetMainWindow();

    /// <summary>
    /// The window a dialog launched right now should be owned by: the currently active
    /// window, falling back to MainWindow. This is what makes a dialog opened from a
    /// secondary window (a report manager, Administration) centre over and dim *that*
    /// window rather than MainWindow (IX-1).
    /// </summary>
    Avalonia.Controls.Window GetActiveWindow();
}
