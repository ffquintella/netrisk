using DAL.Entities;
using Microsoft.Extensions.Hosting;
using Host = DAL.Entities.Host;

namespace GUIClient.ViewModels.Dialogs.Results;

public class HostDialogResult: DialogResultBase
{
    public Host ResultingHost { get; set; }
}