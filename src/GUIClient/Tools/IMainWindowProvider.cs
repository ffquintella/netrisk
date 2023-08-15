using Avalonia.Controls;

namespace GUIClient.Tools;

public interface IMainWindowProvider
{
    Window GetMainWindow();
}