namespace GUIClient.ViewModels.Dialogs.Results;

public class FixRequestDialogResult: DialogResultBase
{
       
    public string? Comments { get; set; }
    public bool? SendToAll { get; set; }
    public string? SendTo { get; set; }
    public int? FixTeamId { get; set; }
    
}