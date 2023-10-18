using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class HostDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; }
    public Host? Host { get; set; }
}