namespace GUIClient.ViewModels.Dialogs.Results;

public class AssessmentRunViewerResult : DialogResultBase
{
    /// <summary>True when the run's draft/progress changed and the list should refresh.</summary>
    public bool RunChanged { get; set; }
}
