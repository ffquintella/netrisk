using DAL.Enums;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>What the operator chose in the triage-lifecycle dialog (Track 3 milestone 3.2.1).</summary>
public class FindingStatusDialogResult : DialogResultBase
{
    public FindingStatus Status { get; set; }

    public string? Justification { get; set; }

    /// <summary>The canonical finding, when the chosen state is Duplicate.</summary>
    public int? DuplicateOfId { get; set; }
}
