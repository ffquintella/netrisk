using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the risk being created or edited.</summary>
public class RiskDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; } = OperationType.Create;

    /// <summary>The existing risk; required for <see cref="OperationType.Edit"/>.</summary>
    public Risk? Risk { get; set; }
}
