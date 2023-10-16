using Model;

namespace GUIClient.ViewModels.Dialogs.Results;

public class CloseDialogResult: DialogResultBase 
{
    public string Comments { get; set; } = "";
    public IntStatus FinalStatus { get; set; } = IntStatus.Closed;
}