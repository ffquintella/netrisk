using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the incident being created or edited.</summary>
public class IncidentDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; } = OperationType.Create;

    /// <summary>The existing incident; required for <see cref="OperationType.Edit"/>.</summary>
    public Incident? Incident { get; set; }
}
