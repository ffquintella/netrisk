using Avalonia.Controls;

namespace GUIClient.Tools;

public interface IMainWindowProvider
{
    Avalonia.Controls.Window GetMainWindow();
}