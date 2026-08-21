using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// Result of the risk editor. Carries the saved risk so the caller can update its collection
/// in place rather than reloading the whole list (IX-6 state sync).
/// </summary>
public class RiskDialogResult: DialogResultBase
{
    public Risk? SavedRisk { get; set; }
}
