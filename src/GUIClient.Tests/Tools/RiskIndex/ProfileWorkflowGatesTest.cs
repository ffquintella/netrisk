using System;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M55, T259–T262 (S40 §6.1–§6.4): every command's enablement and the reason its tooltip shows — the single-draft rule, the
/// save/validate/preview/submit sequence with the 40-character rebaseline justification, and an approval that follows the
/// server's segregation checks before the committee, Board and rebaseline inputs.
/// </summary>
public class ProfileWorkflowGatesTest
{
    private static RiskIndexProfileSummaryDto Summary(int version, RiskIndexProfileStatus status) =>
        new(version, version, "v" + version, "balanced", status, null, null, "ana", null, false, null);

    private static RiskIndexValidationResultDto Validation(bool valid, bool rebaseline = false) =>
        new(valid, [], [], [], rebaseline, rebaseline ? [RiskIndexRebaselineTrigger.PresetChange] : [], false, null);

    private static RiskIndexApprovalEligibilityDto Eligibility(bool passed = true, bool board = false, bool rebaseline = false) =>
        new([new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.SodAuthorCannotApprove, passed,
                passed ? "ok" : "Whoever created or edited the draft cannot approve it.")],
            "role:7", board, rebaseline ? [RiskIndexRebaselineTrigger.OmegaPace] : []);

    private static RiskIndexProfileDto Active(string? committee = "Ata 1") =>
        new(3, 3, "v3", "balanced", RiskIndexMode.Interim, RiskIndexProfileStatus.Active, new RiskIndexParameters(), null, committee,
            null, false, null, "ana", DateTime.UtcNow, null, "bruno", DateTime.UtcNow, "role:7", DateTime.UtcNow, null, null, null,
            null, 1, true);

    [Fact]
    public void TestOnlyOneDraftOrPendingProfileAtATime()
    {
        var profiles = new[] { Summary(1, RiskIndexProfileStatus.Retired), Summary(2, RiskIndexProfileStatus.PendingApproval),
            Summary(3, RiskIndexProfileStatus.Active) };

        Assert.Equal(2, ProfileWorkflowGates.OpenDraft(profiles)!.Version);
        Assert.Equal("RiskAdminOpenDraftExists", ProfileWorkflowGates.NewDraft(true, ProfileWorkflowGates.OpenDraft(profiles)).ReasonKey);
        Assert.True(ProfileWorkflowGates.NewDraft(true, null).Enabled);
        Assert.Equal(RiskIndexAdminAccess.RequiresConfigure, ProfileWorkflowGates.NewDraft(false, null).ReasonKey);
    }

    [Fact]
    public void TestOnlyADraftIsDeletedAndCompareNeedsTwoDistinctVersions()
    {
        Assert.True(ProfileWorkflowGates.DeleteDraft(true, Summary(4, RiskIndexProfileStatus.Draft)).Enabled);
        Assert.Equal("RiskAdminOnlyDraftsDeleted", ProfileWorkflowGates.DeleteDraft(true, Summary(4, RiskIndexProfileStatus.PendingApproval)).ReasonKey);
        Assert.False(ProfileWorkflowGates.DeleteDraft(false, Summary(4, RiskIndexProfileStatus.Draft)).Enabled);
        Assert.False(ProfileWorkflowGates.Compare(Summary(1, RiskIndexProfileStatus.Active), Summary(1, RiskIndexProfileStatus.Active)).Enabled);
        Assert.True(ProfileWorkflowGates.Compare(Summary(1, RiskIndexProfileStatus.Retired), Summary(2, RiskIndexProfileStatus.Active)).Enabled);
    }

    [Theory]
    [InlineData(false, true, false, true, RiskIndexProfileStatus.Draft, "RiskAdminRequiresConfigure")]
    [InlineData(true, true, false, true, RiskIndexProfileStatus.PendingApproval, "RiskAdminDraftPendingApproval")]
    [InlineData(true, false, false, true, RiskIndexProfileStatus.Draft, "RiskAdminNoChanges")]
    [InlineData(true, true, true, true, RiskIndexProfileStatus.Draft, "RiskAdminLoading")]
    [InlineData(true, true, false, false, RiskIndexProfileStatus.Draft, "RiskAdminFixFieldValues")]
    [InlineData(true, true, false, true, RiskIndexProfileStatus.Draft, null)]
    public void TestSaveDraft(bool configure, bool dirty, bool busy, bool valid, RiskIndexProfileStatus status, string? reason)
    {
        var gate = ProfileWorkflowGates.SaveDraft(configure, dirty, busy, valid, status);
        Assert.Equal(reason is null, gate.Enabled);
        Assert.Equal(reason, gate.ReasonKey);
    }

    [Fact]
    public void TestCrossFieldErrorsBlockTheSubmissionNotTheSave()
    {
        // Save is gated by structure only (S40 §6.2.1); an invalid Σω still saves.
        Assert.True(ProfileWorkflowGates.SaveDraft(true, true, false, true, RiskIndexProfileStatus.Draft).Enabled);
        Assert.Equal("RiskAdminFixDraftErrors",
            ProfileWorkflowGates.Submit(true, false, Validation(false), RiskIndexPreviewState.Completed, "motivo").ReasonKey);
    }

    [Fact]
    public void TestValidateAndPreviewNeedASavedDraft()
    {
        Assert.Equal("RiskAdminSaveBeforeValidating", ProfileWorkflowGates.ValidateOnServer(true, true, true).ReasonKey);
        Assert.Equal("RiskAdminNoDraft", ProfileWorkflowGates.ValidateOnServer(true, false, false).ReasonKey);
        Assert.True(ProfileWorkflowGates.ValidateOnServer(true, true, false).Enabled);

        Assert.Equal("RiskAdminSaveDraftFirst", ProfileWorkflowGates.RunPreview(true, true, true, RiskIndexPreviewState.NotRun).ReasonKey);
        Assert.Equal("RiskAdminPreviewRunning", ProfileWorkflowGates.RunPreview(true, true, false, RiskIndexPreviewState.Running).ReasonKey);
        Assert.Equal("RiskAdminPreviewRunning", ProfileWorkflowGates.RunPreview(true, true, false, RiskIndexPreviewState.Queued).ReasonKey);
        Assert.True(ProfileWorkflowGates.RunPreview(true, true, false, RiskIndexPreviewState.Stale).Enabled);
        Assert.Equal(RiskIndexAdminAccess.RequiresConfigure, ProfileWorkflowGates.RunPreview(false, true, false, RiskIndexPreviewState.NotRun).ReasonKey);
    }

    [Fact]
    public void TestSubmitNeedsServerValidityACurrentPreviewAndAJustification()
    {
        Assert.Equal("RiskAdminSaveDraftFirst", ProfileWorkflowGates.Submit(true, true, Validation(true), RiskIndexPreviewState.Completed, "x").ReasonKey);
        Assert.Equal("RiskAdminValidateFirst", ProfileWorkflowGates.Submit(true, false, null, RiskIndexPreviewState.Completed, "x").ReasonKey);
        Assert.Equal("RiskAdminRunPreviewFirst", ProfileWorkflowGates.Submit(true, false, Validation(true), RiskIndexPreviewState.Stale, "x").ReasonKey);
        Assert.Equal("RiskAdminWriteJustification", ProfileWorkflowGates.Submit(true, false, Validation(true), RiskIndexPreviewState.Completed, "  ").ReasonKey);
        Assert.True(ProfileWorkflowGates.Submit(true, false, Validation(true), RiskIndexPreviewState.Completed, "Calibração").Enabled);
    }

    [Fact]
    public void TestARebaselineProposalIsSubmittedWithFortyCharactersOfJustification()
    {
        var shortText = new string('a', ProfileWorkflowGates.RebaselineJustificationLength - 1);
        var longText = new string('a', ProfileWorkflowGates.RebaselineJustificationLength);

        Assert.Equal("RiskAdminRebaselineJustification",
            ProfileWorkflowGates.Submit(true, false, Validation(true, rebaseline: true), RiskIndexPreviewState.Completed, shortText).ReasonKey);
        Assert.True(ProfileWorkflowGates.Submit(true, false, Validation(true, rebaseline: true), RiskIndexPreviewState.Completed, longText).Enabled);
    }

    [Fact]
    public void TestApprovalFollowsTheServersFirstFailingCheckWithItsOwnSentence()
    {
        var gate = ProfileWorkflowGates.Approve(true, RiskIndexProfileStatus.PendingApproval, Eligibility(passed: false), "Ata", 1, null, false);

        Assert.False(gate.Enabled);
        Assert.Null(gate.ReasonKey);
        Assert.Equal("Whoever created or edited the draft cannot approve it.", gate.ReasonText);
    }

    [Fact]
    public void TestApprovalNeedsTheCommitteeWithMinutesTheBoardWhenRequiredAndTheRebaselineConfirmation()
    {
        var pending = RiskIndexProfileStatus.PendingApproval;
        Assert.Equal("RiskAdminCommitteeRequired", ProfileWorkflowGates.Approve(true, pending, Eligibility(), "", 1, null, false).ReasonKey);
        Assert.Equal("RiskAdminCommitteeRequired", ProfileWorkflowGates.Approve(true, pending, Eligibility(), "Ata", 0, null, false).ReasonKey);
        Assert.Equal("RiskAdminBoardRequired", ProfileWorkflowGates.Approve(true, pending, Eligibility(board: true), "Ata", 1, null, false).ReasonKey);
        Assert.Equal("RiskAdminConfirmRebaseline",
            ProfileWorkflowGates.Approve(true, pending, Eligibility(rebaseline: true), "Ata", 1, null, false).ReasonKey);
        Assert.True(ProfileWorkflowGates.Approve(true, pending, Eligibility(board: true, rebaseline: true), "Ata", 1, "Conselho", true).Enabled);
        Assert.Equal("RiskAdminLoading", ProfileWorkflowGates.Approve(true, pending, null, "Ata", 1, null, false).ReasonKey);
        Assert.Equal("RiskAdminNotPendingApproval",
            ProfileWorkflowGates.Approve(true, RiskIndexProfileStatus.Draft, Eligibility(), "Ata", 1, null, false).ReasonKey);
        Assert.Equal(RiskIndexAdminAccess.RequiresApprove, ProfileWorkflowGates.Approve(false, pending, Eligibility(), "Ata", 1, null, false).ReasonKey);
    }

    [Fact]
    public void TestRejectAndAttachNeedAPendingProfileAndRejectAReason()
    {
        var pending = RiskIndexProfileStatus.PendingApproval;
        Assert.Equal("RiskAdminWriteReason", ProfileWorkflowGates.Reject(true, pending, " ").ReasonKey);
        Assert.True(ProfileWorkflowGates.Reject(true, pending, "Pesos fora do acordado").Enabled);
        Assert.Equal("RiskAdminNotPendingApproval", ProfileWorkflowGates.Reject(true, RiskIndexProfileStatus.Active, "x").ReasonKey);
        Assert.True(ProfileWorkflowGates.AttachEvidence(true, pending).Enabled);
        Assert.False(ProfileWorkflowGates.AttachEvidence(false, pending).Enabled);
    }

    [Fact]
    public void TestPublishNeedsTheShadowAndAnApprovedActiveProfileAndSuspendItsReasons()
    {
        Assert.True(ProfileWorkflowGates.Publish(true, RiskIndexPublicationState.Shadow, Active()).Enabled);
        Assert.Equal("RiskAdminAlreadyPublished", ProfileWorkflowGates.Publish(true, RiskIndexPublicationState.Published, Active()).ReasonKey);
        Assert.Equal("RiskAdminNeedsApprovedActive", ProfileWorkflowGates.Publish(true, RiskIndexPublicationState.Shadow, null).ReasonKey);
        Assert.Equal("RiskAdminNeedsApprovedActive", ProfileWorkflowGates.Publish(true, RiskIndexPublicationState.Shadow, Active(null)).ReasonKey);

        Assert.Equal("RiskAdminNoActiveProfile", ProfileWorkflowGates.Suspend(true, null, "x", "y").ReasonKey);
        Assert.Equal("RiskAdminSuspendNeedsReasons", ProfileWorkflowGates.Suspend(true, Active(), "x", " ").ReasonKey);
        Assert.True(ProfileWorkflowGates.Suspend(true, Active(), "Defeito no tau", "Ata 2").Enabled);
        Assert.Equal(RiskIndexAdminAccess.RequiresApprove, ProfileWorkflowGates.Suspend(false, Active(), "x", "y").ReasonKey);
    }
}
