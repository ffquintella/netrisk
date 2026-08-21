using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>Result of the incident-response-plan editor.</summary>
public class IrpDialogResult: DialogResultBase
{
    public IncidentResponsePlan? Plan { get; set; }

    public bool WasCreated { get; set; }
}
