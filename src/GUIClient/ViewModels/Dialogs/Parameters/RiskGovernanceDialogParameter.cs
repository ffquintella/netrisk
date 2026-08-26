namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the risk whose governance record is being opened (Track 8).</summary>
public class RiskGovernanceDialogParameter : NavigationParameterBase
{
    public int RiskId { get; set; }

    public string RiskSubject { get; set; } = string.Empty;
}
