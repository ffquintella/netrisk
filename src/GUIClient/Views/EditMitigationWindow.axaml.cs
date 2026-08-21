using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class EditMitigationWindow : DialogWindowBase<MitigationDialogResult>
{
    public EditMitigationWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
