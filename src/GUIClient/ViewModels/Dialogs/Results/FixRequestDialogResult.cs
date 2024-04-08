namespace GUIClient.ViewModels.Dialogs.Results;

public class FixRequestDialogResult: DialogResultBase
{
    
    public string? Comment { get; set; }
    
    public bool? SendToAll { get; set; }
    public string? SendTo { get; set; }
    
    public string? EmailMessage { get; set; }
}