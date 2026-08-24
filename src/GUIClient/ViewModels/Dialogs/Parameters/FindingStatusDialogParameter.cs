using System.Collections.Generic;
using DAL.Enums;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>
/// Input to the triage-lifecycle dialog (Track 3 milestone 3.2.1).
///
/// <see cref="AllowedStatuses"/> comes from the server rather than being derived client-side: the
/// transition matrix is enforced there, and offering a state the server will refuse wastes the
/// operator's time and teaches them to distrust the UI.
/// </summary>
public class FindingStatusDialogParameter : NavigationParameterBase
{
    public int FindingId { get; set; }

    public string? FindingTitle { get; set; }

    public FindingStatus CurrentStatus { get; set; }

    public List<FindingStatus> AllowedStatuses { get; set; } = new();
}
