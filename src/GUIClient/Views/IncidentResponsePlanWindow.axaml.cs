using System;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

public partial class IncidentResponsePlanWindow : DialogWindowBase<IrpDialogResult>
{
    public IncidentResponsePlanWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Disposes the view-model however the dialog was dismissed. This used to be a
    /// <c>Closed</c> handler declared in XAML that then called <c>Close()</c> on the window again.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IncidentResponsePlanViewModel)?.OnClose();

        base.OnClosed(e);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
