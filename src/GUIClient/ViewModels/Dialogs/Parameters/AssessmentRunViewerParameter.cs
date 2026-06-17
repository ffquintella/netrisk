using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class AssessmentRunViewerParameter : NavigationParameterBase
{
    public Assessment? Assessment { get; set; }

    public AssessmentRun? AssessmentRun { get; set; }
}
