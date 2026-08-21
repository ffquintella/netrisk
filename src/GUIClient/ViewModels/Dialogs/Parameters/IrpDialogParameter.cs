using DAL.Entities;
using GUIClient.Models;

namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the incident-response plan being created, edited or viewed.</summary>
public class IrpDialogParameter: NavigationParameterBase
{
    public OperationType Operation { get; set; } = OperationType.Create;

    public IncidentResponsePlan? Plan { get; set; }

    /// <summary>The risk the plan is being created for; required for <see cref="OperationType.Create"/>.</summary>
    public Risk? RelatedRisk { get; set; }

    /// <summary>Set only by unit tests, to suppress the reference-data load.</summary>
    public bool TestOnly { get; set; }
}
