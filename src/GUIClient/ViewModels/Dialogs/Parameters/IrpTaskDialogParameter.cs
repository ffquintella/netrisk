using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the incident-response plan and, when editing or viewing, the task within it.</summary>
public class IrpTaskDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; } = OperationType.Create;

    public IncidentResponsePlan? Plan { get; set; }

    /// <summary>The existing task; required for <see cref="OperationType.Edit"/> and <see cref="OperationType.View"/>.</summary>
    public IncidentResponsePlanTask? Task { get; set; }
}
