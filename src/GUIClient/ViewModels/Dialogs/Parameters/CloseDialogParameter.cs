using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class CloseDialogParameter: NavigationParameterBase
{
    public string? Title { get; set; }
    public OperationType OperationType { get; set; } = OperationType.Create;
    public string Comments { get; set; } = "";
     
}