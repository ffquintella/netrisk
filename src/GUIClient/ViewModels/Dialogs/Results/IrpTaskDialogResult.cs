using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// Result of the IRP task editor. The caller updates its task collection in place from
/// <see cref="Task"/> — inserting when <see cref="WasCreated"/>, replacing otherwise (IX-2).
/// </summary>
public class IrpTaskDialogResult: DialogResultBase
{
    public IncidentResponsePlanTask? Task { get; set; }

    public bool WasCreated { get; set; }
}
