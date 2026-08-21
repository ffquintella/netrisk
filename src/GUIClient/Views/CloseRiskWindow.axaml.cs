using Avalonia.Markup.Xaml;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class CloseRiskWindow : DialogWindowBase<CloseRiskDialogResult>
{
    public CloseRiskWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
