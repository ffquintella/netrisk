using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the risk the close dialog is committing a closure for.</summary>
public class CloseRiskDialogParameter: NavigationParameterBase
{
    public Risk? Risk { get; set; }
}
