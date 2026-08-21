using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Admin;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Admin;

public partial class AddFaceImage : DialogWindowBase<FaceImageDialogResult>
{
    public AddFaceImage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Releases the capture device when the dialog goes away, however it was dismissed —
    /// this used to be wired by the view-model hooking the window's Closed event.
    /// </summary>
    protected override void OnClosed(System.EventArgs e)
    {
        if (DataContext is AddFaceImageViewModel viewModel)
        {
            _ = viewModel.DisposeAsync();
        }

        base.OnClosed(e);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
