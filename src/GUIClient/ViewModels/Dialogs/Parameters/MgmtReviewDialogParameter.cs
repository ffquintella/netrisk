using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the risk being reviewed and whether a new review is being added or the last one amended.</summary>
public class MgmtReviewDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; } = OperationType.Create;
    public int RiskId { get; set; }
}
