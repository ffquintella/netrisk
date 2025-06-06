using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace GUIClient.Tools.Window;

public static class WindowPositioning
{
    public static void CenterOnScreen(Avalonia.Controls.Window window)
    {
        if (window.Screens is null)
            return;

        var screen = window.Screens.ScreenFromVisual(window);
        var screenBounds = screen.WorkingArea;

        var x = screenBounds.X + (screenBounds.Width  - window.Width)  / 2;
        var y = screenBounds.Y + (screenBounds.Height - window.Height) / 2;

        window.Position = new PixelPoint((int)x, (int)y);
    }
}
