using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class AssessmentRunViewerParameter : NavigationParameterBase
{
    public Assessment? Assessment { get; set; }

    public AssessmentRun? AssessmentRun { get; set; }

    /// <summary>
    /// When true the viewer runs in read/answer-only PREVIEW mode: questions are paged and
    /// answerable so the author can see how the questionnaire will look, but nothing is
    /// persisted (no draft saves, no run state, no submit). No <see cref="AssessmentRun"/> is required.
    /// </summary>
    public bool IsPreview { get; set; }
}
