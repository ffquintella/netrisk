namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>Identifies the user whose face image is being captured.</summary>
public class FaceImageDialogParameter: NavigationParameterBase
{
    public int UserId { get; set; }
}
