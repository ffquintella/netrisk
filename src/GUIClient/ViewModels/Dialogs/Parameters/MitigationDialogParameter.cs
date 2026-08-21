using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the risk whose mitigation is being planned or revised.</summary>
public class MitigationDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; } = OperationType.Create;
    public int RiskId { get; set; }

    /// <summary>The existing mitigation; required for <see cref="OperationType.Edit"/>.</summary>
    public Mitigation? Mitigation { get; set; }
}
