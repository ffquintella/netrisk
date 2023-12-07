using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class AssessmentRunDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; }
    public Assessment? Assessment { get; set; }
}