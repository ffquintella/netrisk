using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// Whether a command of the configuration screens is enabled and, when it is not, why (S40 §6.1–§6.4, IX-4): every gate
/// answers a resource key the button's tooltip shows, or, for the approval, the server's own sentence. Enabling only — the
/// API refuses whatever slips through, with the same codes.
/// </summary>
public readonly record struct Gate(bool Enabled, string? ReasonKey, string? ReasonText = null)
{
    public static readonly Gate Open = new(true, null);

    public static Gate Closed(string reasonKey) => new(false, reasonKey);

    public static Gate ClosedWithText(string text) => new(false, null, text);
}

public static class ProfileWorkflowGates
{
    /// <summary>The justification a rebaseline proposal needs on submission (S40 §6.2.3: <c>rebaseline_reason_required</c>).</summary>
    public const int RebaselineJustificationLength = 40;

    public static Gate NewDraft(bool canConfigure, RiskIndexProfileSummaryDto? openDraft) =>
        !canConfigure ? Gate.Closed(RiskIndexAdminAccess.RequiresConfigure)
        : openDraft is not null ? Gate.Closed("RiskAdminOpenDraftExists")
        : Gate.Open;

    /// <summary>The draft in progress: at most one profile is a draft or pending approval at a time (S40 §6.1).</summary>
    public static RiskIndexProfileSummaryDto? OpenDraft(IEnumerable<RiskIndexProfileSummaryDto> profiles) =>
        profiles.Where(p => p.Status is RiskIndexProfileStatus.Draft or RiskIndexProfileStatus.PendingApproval)
            .OrderByDescending(p => p.Version).FirstOrDefault();

    public static Gate DeleteDraft(bool canConfigure, RiskIndexProfileSummaryDto? selected) =>
        !canConfigure ? Gate.Closed(RiskIndexAdminAccess.RequiresConfigure)
        : selected?.Status != RiskIndexProfileStatus.Draft ? Gate.Closed("RiskAdminOnlyDraftsDeleted")
        : Gate.Open;

    public static Gate Compare(RiskIndexProfileSummaryDto? a, RiskIndexProfileSummaryDto? b) =>
        a is null || b is null || a.Id == b.Id ? Gate.Closed("RiskAdminSelectTwoVersions") : Gate.Open;

    /// <summary>Save: dirty, idle, every field parses, and the profile is still a draft (S40 §6.2.1).</summary>
    public static Gate SaveDraft(bool canConfigure, bool isDirty, bool isBusy, bool structurallyValid,
        RiskIndexProfileStatus? status)
    {
        if (!canConfigure) return Gate.Closed(RiskIndexAdminAccess.RequiresConfigure);
        if (status is RiskIndexProfileStatus.PendingApproval) return Gate.Closed("RiskAdminDraftPendingApproval");
        if (status is not null and not RiskIndexProfileStatus.Draft) return Gate.Closed("RiskAdminNoDraft");
        if (!isDirty) return Gate.Closed("RiskAdminNoChanges");
        if (isBusy) return Gate.Closed("RiskAdminLoading");
        return structurallyValid ? Gate.Open : Gate.Closed("RiskAdminFixFieldValues");
    }

    public static Gate ValidateOnServer(bool canConfigure, bool hasDraft, bool isDirty) =>
        !canConfigure ? Gate.Closed(RiskIndexAdminAccess.RequiresConfigure)
        : !hasDraft ? Gate.Closed("RiskAdminNoDraft")
        : isDirty ? Gate.Closed("RiskAdminSaveBeforeValidating")
        : Gate.Open;

    public static Gate RunPreview(bool canConfigure, bool hasDraft, bool isDirty, RiskIndexPreviewState state) =>
        !canConfigure ? Gate.Closed(RiskIndexAdminAccess.RequiresConfigure)
        : !hasDraft ? Gate.Closed("RiskAdminNoDraft")
        : isDirty ? Gate.Closed("RiskAdminSaveDraftFirst")
        : state is RiskIndexPreviewState.Queued or RiskIndexPreviewState.Running ? Gate.Closed("RiskAdminPreviewRunning")
        : Gate.Open;

    /// <summary>
    /// Submit (S40 §6.3): the server said the saved draft is valid (a rebaseline proposal may still be submitted), the
    /// preview completed for these parameters (the server reports <see cref="RiskIndexPreviewState.Stale"/> otherwise), and a
    /// justification is written — 40 characters when the submission is a rebaseline proposal.
    /// </summary>
    public static Gate Submit(bool canConfigure, bool isDirty, RiskIndexValidationResultDto? validation,
        RiskIndexPreviewState previewState, string? justification)
    {
        if (!canConfigure) return Gate.Closed(RiskIndexAdminAccess.RequiresConfigure);
        if (isDirty) return Gate.Closed("RiskAdminSaveDraftFirst");
        if (validation is null) return Gate.Closed("RiskAdminValidateFirst");
        if (!validation.IsValid) return Gate.Closed("RiskAdminFixDraftErrors");
        if (previewState != RiskIndexPreviewState.Completed) return Gate.Closed("RiskAdminRunPreviewFirst");
        var length = justification?.Trim().Length ?? 0;
        if (length == 0) return Gate.Closed("RiskAdminWriteJustification");
        if (validation.RequiresRebaseline && length < RebaselineJustificationLength)
            return Gate.Closed("RiskAdminRebaselineJustification");
        return Gate.Open;
    }

    /// <summary>
    /// Approve (S40 §6.4): every server-computed segregation check passes — the first failing one's own sentence is the
    /// reason, so the screen never words a refusal differently from the API — then the committee reference and its minutes,
    /// the Board reference when required, and the rebaseline confirmation when there is a trigger.
    /// </summary>
    public static Gate Approve(bool canApprove, RiskIndexProfileStatus? status, RiskIndexApprovalEligibilityDto? eligibility,
        string? committeeRef, int evidenceCount, string? boardRef, bool confirmRebaseline)
    {
        if (!canApprove) return Gate.Closed(RiskIndexAdminAccess.RequiresApprove);
        if (status is not RiskIndexProfileStatus.PendingApproval) return Gate.Closed("RiskAdminNotPendingApproval");
        if (eligibility is null) return Gate.Closed("RiskAdminLoading");
        if (eligibility.Checks.FirstOrDefault(c => !c.Passed) is { } failed)
            return string.IsNullOrWhiteSpace(failed.Message)
                ? Gate.Closed("RiskApprovalCheck_" + failed.Code)
                : Gate.ClosedWithText(failed.Message);
        if (string.IsNullOrWhiteSpace(committeeRef) || evidenceCount == 0) return Gate.Closed("RiskAdminCommitteeRequired");
        if (eligibility.RequiresBoardRef && string.IsNullOrWhiteSpace(boardRef)) return Gate.Closed("RiskAdminBoardRequired");
        if (eligibility.RebaselineTriggers.Count > 0 && !confirmRebaseline) return Gate.Closed("RiskAdminConfirmRebaseline");
        return Gate.Open;
    }

    public static Gate Reject(bool canApprove, RiskIndexProfileStatus? status, string? reason) =>
        !canApprove ? Gate.Closed(RiskIndexAdminAccess.RequiresApprove)
        : status is not RiskIndexProfileStatus.PendingApproval ? Gate.Closed("RiskAdminNotPendingApproval")
        : string.IsNullOrWhiteSpace(reason) ? Gate.Closed("RiskAdminWriteReason")
        : Gate.Open;

    public static Gate AttachEvidence(bool canApprove, RiskIndexProfileStatus? status) =>
        !canApprove ? Gate.Closed(RiskIndexAdminAccess.RequiresApprove)
        : status is not RiskIndexProfileStatus.PendingApproval ? Gate.Closed("RiskAdminNotPendingApproval")
        : Gate.Open;

    /// <summary>Publish: the shadow pilot, and an active profile carrying the committee's decision (S40 §6.4).</summary>
    public static Gate Publish(bool canApprove, RiskIndexPublicationState? publication, RiskIndexProfileDto? active) =>
        !canApprove ? Gate.Closed(RiskIndexAdminAccess.RequiresApprove)
        : publication is not RiskIndexPublicationState.Shadow ? Gate.Closed("RiskAdminAlreadyPublished")
        : active is null || string.IsNullOrWhiteSpace(active.CommitteeDecisionRef) ? Gate.Closed("RiskAdminNeedsApprovedActive")
        : Gate.Open;

    /// <summary>Suspend publication: retire the active profile, with a justification and the committee's reference.</summary>
    public static Gate Suspend(bool canApprove, RiskIndexProfileDto? active, string? justification, string? committeeRef) =>
        !canApprove ? Gate.Closed(RiskIndexAdminAccess.RequiresApprove)
        : active is null ? Gate.Closed("RiskAdminNoActiveProfile")
        : string.IsNullOrWhiteSpace(justification) || string.IsNullOrWhiteSpace(committeeRef)
            ? Gate.Closed("RiskAdminSuspendNeedsReasons")
            : Gate.Open;
}
