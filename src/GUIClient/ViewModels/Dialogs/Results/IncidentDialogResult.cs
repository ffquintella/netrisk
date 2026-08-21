using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// Result of the incident editor. The caller updates its collection in place from
/// <see cref="Incident"/> — inserting when <see cref="WasCreated"/>, replacing otherwise —
/// instead of the dialog raising events into the parent (IX-2).
/// </summary>
public class IncidentDialogResult: DialogResultBase
{
    public Incident? Incident { get; set; }

    public bool WasCreated { get; set; }
}
