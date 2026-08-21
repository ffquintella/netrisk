using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class IncidentResponsePlanTaskWindow : DialogWindowBase<IrpTaskDialogResult>
{
    public IncidentResponsePlanTaskWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
