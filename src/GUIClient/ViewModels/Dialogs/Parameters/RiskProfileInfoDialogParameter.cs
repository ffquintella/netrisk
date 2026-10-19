namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>The profile the Cyber Risk Overview's profile chip opens on: the one of the snapshot shown (S40 §5.1).</summary>
public class RiskProfileInfoDialogParameter : NavigationParameterBase
{
    public int ProfileId { get; set; }
}
