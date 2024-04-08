namespace GUIClient.ViewModels.Dialogs.Parameters;

public class FixRequestDialogParameter: NavigationParameterBase
{
    public string? Vulnerability { get; set; }
    public float Score { get; set; } = 0;
    public string? Solution { get; set; }
    public string? Comments { get; set; }
    public int? FixTeamId { get; set; }
}