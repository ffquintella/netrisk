namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the risk whose linkage chain is being edited (Stage 9.1, S41 §7).</summary>
public class EditRiskChainDialogParameter : NavigationParameterBase
{
    public int RiskId { get; set; }

    public string RiskSubject { get; set; } = string.Empty;
}
