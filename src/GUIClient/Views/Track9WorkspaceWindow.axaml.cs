using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class Track9WorkspaceWindow : AuxiliaryWindowBase
{
    public Track9WorkspaceWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as Track9WorkspaceViewModel)?.Dispose();
    }
}
