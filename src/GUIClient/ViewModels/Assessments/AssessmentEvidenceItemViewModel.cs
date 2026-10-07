using System;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using System.Threading.Tasks;
using GUIClient.Tools;
using Model.Assessments;
using ReactiveUI;

namespace GUIClient.ViewModels.Assessments;

/// <summary>
/// One evidence file under a question of the run viewer (GitHub #80, S44): its name and icon, and the
/// download and delete commands, which the viewer supplies because they need its services and window.
/// </summary>
public class AssessmentEvidenceItemViewModel : ReactiveObject
{
    public AssessmentAnswerEvidence Evidence { get; }

    public string Name => Evidence.Name;

    /// <summary>The MIME type the icon is chosen by, never empty.</summary>
    public string Type => AssessmentEvidenceSummary.IconType(Evidence.Type);

    /// <summary>Delete is offered only on an open run.</summary>
    public bool CanDelete { get; }

    public ReactiveCommand<RxVoid, RxVoid> DownloadCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public AssessmentEvidenceItemViewModel(AssessmentAnswerEvidence evidence, bool canDelete,
        Func<AssessmentEvidenceItemViewModel, Task> download,
        Func<AssessmentEvidenceItemViewModel, Task> delete)
    {
        Evidence = evidence;
        CanDelete = canDelete;
        DownloadCommand = ReactiveCommand.CreateFromTask(() => download(this));
        DeleteCommand = ReactiveCommand.CreateFromTask(() => delete(this));
    }
}
