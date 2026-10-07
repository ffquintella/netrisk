namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>Whether a Stage 9.3 dialog changed anything, so the continuity panel reloads only when it has to.</summary>
public class ContinuityDialogResult : DialogResultBase
{
    public bool Changed { get; set; }
}
