namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// What the governance dialog changed, so the risk list can refresh the row rather than reloading
/// everything (IX-2: the caller updates its own state from a typed result).
/// </summary>
public class RiskGovernanceDialogResult : DialogResultBase
{
    /// <summary>True when an acceptance was created, renewed or revoked.</summary>
    public bool AcceptanceChanged { get; set; }

    /// <summary>True when the residual score moved — a quantitative run rewrites it.</summary>
    public bool ScoresChanged { get; set; }
}
