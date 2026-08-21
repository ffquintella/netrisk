using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// Result of the management-review dialog. The saved review travels back as a typed result so
/// the caller updates its own state (IX-2) instead of the dialog raising an event into it.
/// </summary>
public class MgmtReviewDialogResult: DialogResultBase
{
    public MgmtReview? SavedReview { get; set; }

    /// <summary>The next step the reviewer chose, so the caller can offer it (IX-6 next-step affordance).</summary>
    public int? NextStep { get; set; }

    /// <summary>Human-readable name of <see cref="NextStep"/>, for the follow-up prompt.</summary>
    public string? NextStepName { get; set; }
}
