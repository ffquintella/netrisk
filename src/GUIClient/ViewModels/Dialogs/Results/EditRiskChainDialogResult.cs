namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// Whether the chain dialog changed anything, so the risk detail reloads its chain block only when
/// it has to. Every edit in the dialog is saved the moment it is made — there is no batch to commit
/// or discard (S41 §11, D12) — so this is a report, not a decision.
/// </summary>
public class EditRiskChainDialogResult : DialogResultBase
{
    public bool Changed { get; set; }
}
