using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class AssessmentRunDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; }
}