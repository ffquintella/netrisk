namespace GUIClient.ViewModels.Dialogs.Results;

public class StringDialogResult: DialogResultBase
{
    public string? Result { get; set; } 
    public ResultActions? Action { get; set; }
}