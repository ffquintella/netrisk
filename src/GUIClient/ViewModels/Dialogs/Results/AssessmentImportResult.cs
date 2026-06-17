using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

public class AssessmentImportResult : DialogResultBase
{
    public Assessment? ImportedAssessment { get; set; }
}
