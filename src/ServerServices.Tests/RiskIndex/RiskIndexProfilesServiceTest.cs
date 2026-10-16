using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Profiles;
using ServerServices.RiskIndex.Read;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// An in-memory DAL that attributes every save to <see cref="ActingUserId"/>, as the API's does to the caller — so the
/// profile's audit trail names its editors, which the approval's segregation of duties reads (S40 §6.4).
/// </summary>
internal sealed class ActingDalService(string databaseName) : IDalService
{
    private readonly InMemoryDalService _inner = new(databaseName);

    public int ActingUserId { get; set; }

    public EntityScope Scope
    {
        get => _inner.Scope;
        set => _inner.Scope = value;
    }

    public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
    {
        var context = _inner.GetContext(withIdentity, bypassEntityScope);
        if (withIdentity && ActingUserId > 0)
        {
            context.UserId = ActingUserId;
            context.AuditActor = "user" + ActingUserId;
        }

        return context;
    }

    public EntityScope GetCurrentEntityScope() => _inner.Scope;
}

/// <summary>
/// M53, T250 (S39 §12.4, §13.1–§13.2; S40 §6.2–§6.4, §8.4, §11.4, §12.2): the lifecycle of a profile — draft, revision,
/// preview, submission, the segregated approval with each refusal pinned by a negative case, rejection, suspension, and the
/// global-scope and shadow rules of what a profile produced.
/// </summary>
[TestSubject(typeof(RiskIndexProfilesService))]
public partial class RiskIndexProfilesServiceTest : InMemoryServiceTestBase
{
    private const int Author = 10, Approver = 20, SecondApprover = 21, AdminUser = 30, DirectGrant = 40, CommitteeMember = 50,
        BusinessReviewer = 60, NamedReviewer = 61, OtherAnalyst = 70;

    private const int AnalystRole = 101, ManagerRole = 102, RiskAdminRole = 103, AdministratorRole = 104, PlainRole = 105,
        CommitteeRole = 106;

    private readonly ActingDalService _dal;

    private IRiskIndexProfilesService Service => GetService<IRiskIndexProfilesService>();

    public RiskIndexProfilesServiceTest() : base(name => new ActingDalService(name))
    {
        _dal = (ActingDalService)GetService<IDalService>();
        SeedUnscoped(ctx =>
        {
            var keys = new[]
            {
                RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve, "entity_risk_context",
                RiskIndexProfilesService.BusinessRiskReview
            };
            var permissions = keys.Select((k, i) => new Permission { Id = 900 + i, Key = k, Name = k, Description = "" })
                .ToDictionary(p => p.Key);
            ctx.Permissions.AddRange(permissions.Values);

            Permission[] Grants(params string[] k) => k.Select(x => permissions[x]).ToArray();
            ctx.Roles.AddRange(
                new Role { Value = AnalystRole, Name = RiskIndexRoles.RiskAnalyst, Permissions = Grants(RiskIndexPermissions.View, RiskIndexPermissions.Configure) },
                new Role { Value = ManagerRole, Name = RiskIndexRoles.RiskManager, Permissions = Grants(RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve, "entity_risk_context") },
                new Role { Value = RiskAdminRole, Name = RiskIndexRoles.RiskAdministrator, Permissions = Grants(RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve, "entity_risk_context") },
                new Role { Value = AdministratorRole, Name = RiskIndexRoles.Administrator, Admin = true },
                new Role { Value = PlainRole, Name = "Plain" },
                new Role { Value = CommitteeRole, Name = "Committee", Permissions = Grants(RiskIndexPermissions.View, RiskIndexPermissions.Approve) });

            ctx.Users.AddRange(
                NewUser(Author, AnalystRole), NewUser(OtherAnalyst, AnalystRole), NewUser(Approver, ManagerRole),
                NewUser(SecondApprover, RiskAdminRole), NewUser(AdminUser, AdministratorRole, admin: true),
                NewUser(OtherAdmin, AdministratorRole, admin: true),
                NewUser(DirectGrant, PlainRole, Grants(RiskIndexPermissions.View, RiskIndexPermissions.Approve)),
                NewUser(CommitteeMember, CommitteeRole),
                NewUser(BusinessReviewer, ManagerRole, Grants(RiskIndexProfilesService.BusinessRiskReview)),
                NewUser(NamedReviewer, ManagerRole));

            ctx.Entities.Add(new Entity
            {
                Id = 500, DefinitionName = "organizationUnit", DefinitionVersion = "2.7", Status = "active",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow
            });
            ctx.EntityRiskReviewers.Add(new EntityRiskReviewer { Id = 1, EntityId = 500, UserId = NamedReviewer, CreatedAt = DateTime.UtcNow });
            ctx.Settings.Add(new Setting { Name = RiskIndexSettingKeys.ApproverRoles, Value = RiskIndexRoles.DefaultApproverRoles });
        });
    }

    private static User NewUser(int id, int role, Permission[]? direct = null, bool admin = false) => new()
    {
        Value = id, Name = $"user {id}", Login = $"u{id}", Enabled = true, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"u{id}@example.invalid", RoleId = role, Admin = admin,
        Permissions = direct ?? []
    };

    /// <summary>The token's claims: the role's grants and the direct ones, as the authentication handler builds them.</summary>
    private RiskIndexCaller As(int user)
    {
        _dal.ActingUserId = user;
        using var db = OpenContext();
        var row = db.Users.AsNoTracking().Include(u => u.Permissions).Single(u => u.Value == user);
        var role = db.Roles.AsNoTracking().Include(r => r.Permissions).Single(r => r.Value == row.RoleId);
        return new RiskIndexCaller(user, row.Admin,
            role.Permissions.Select(p => p.Key).Concat(row.Permissions.Select(p => p.Key)).ToHashSet());
    }

    private async Task CompletePreviewAsync(int id, int user)
    {
        await Service.RequestPreviewAsync(id, As(user));
        Assert.Equal(RiskIndexPreviewRunResult.Completed, await GetService<IRiskIndexPreviewService>().ProcessNextAsync(DateTime.UtcNow));
    }

    /// <summary>The first profile, drafted from a preset, previewed and submitted by <see cref="Author"/>.</summary>
    private async Task<RiskIndexProfileDto> SubmittedAsync(int author = Author)
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("Pilot", null, RiskIndexPresets.Balanced), As(author));
        await CompletePreviewAsync(draft.Id, author);
        return await Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("Pilot profile for the shadow period."), As(author));
    }

    private static RiskIndexApproveRequest Approval(bool rebaseline = false) =>
        new("CRTI 2026-10, item 4", "Board 2026-10", rebaseline, null);

    private int _fileId = 7000;

    /// <summary>An upload by <paramref name="owner"/> through <c>/Files</c>, with the parent the request named (if any).</summary>
    private int Upload(int owner, int? riskId = null)
    {
        var id = ++_fileId;
        SeedUnscoped(ctx => ctx.NrFiles.Add(new NrFile
        {
            Id = id, Name = "ata-crti.pdf", UniqueName = "ata-" + id, Type = "1", Size = 1, Timestamp = DateTime.UtcNow,
            User = owner, Content = [1], RiskId = riskId
        }));
        return id;
    }

    /// <summary>The committee's minutes, uploaded by <paramref name="approver"/> and linked to the profile (S40 §6.4).</summary>
    private async Task AttachMinutesAsync(int profile, int approver = Approver) =>
        await Service.AttachAsync(profile, new RiskIndexAttachRequest(Upload(approver)), As(approver));

    private static async Task<RiskIndexException> Refused(Func<Task> action) => await Assert.ThrowsAsync<RiskIndexException>(action);

    // --- draft ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheFirstProfileStartsFromAnActivatablePreset()
    {
        var noPreset = await Refused(() => Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, null), As(Author)));
        var pureMean = await Refused(() =>
            Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.PureMean), As(Author)));
        var unknown = await Refused(() => Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, "x"), As(Author)));

        Assert.Equal((400, RiskIndexErrors.PresetRequiredForFirstProfile), (noPreset.Status, noPreset.Code));
        Assert.Equal(RiskIndexErrors.PresetNotActivatable, pureMean.Code);
        Assert.Equal(RiskIndexErrors.UnknownPreset, unknown.Code);

        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Ctem), As(Author));
        Assert.Equal((1, RiskIndexProfileStatus.Draft, 0.40), (draft.Version, draft.Status, draft.Parameters.Omega.Exp));
        Assert.Equal($"user {Author}", draft.CreatedBy);
    }

    [Fact]
    public async Task TestOneOpenDraftAtATime()
    {
        await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));

        var second = await Refused(() =>
            Service.CreateAsync(new RiskIndexProfileCreateRequest("v2", null, RiskIndexPresets.Balanced), As(OtherAnalyst)));

        Assert.Equal((409, RiskIndexErrors.OpenDraftExists), (second.Status, second.Code));
    }

    /// <summary>The negative case the brief names: a stale revision is a conflict, never a silent overwrite.</summary>
    [Fact]
    public async Task TestAStaleRevisionIsAConflict()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        var edited = await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1b", RiskIndexMode.Interim,
            draft.Parameters with { P = 6 }), draft.Revision, As(Author));
        Assert.Equal(draft.Revision + 1, edited.Revision);

        var stale = await Refused(() => Service.UpdateAsync(draft.Id,
            new RiskIndexProfileUpdateRequest("v1c", RiskIndexMode.Interim, draft.Parameters), draft.Revision, As(OtherAnalyst)));

        Assert.Equal((409, RiskIndexErrors.StaleDraft), (stale.Status, stale.Code));
        Assert.Equal("v1b", (await Service.GetAsync(draft.Id)).Name);
    }

    [Fact]
    public async Task TestACompletedPreviewDoesNotMakeTheDraftStale()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await CompletePreviewAsync(draft.Id, Author);

        var edited = await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1", RiskIndexMode.Interim,
            draft.Parameters with { P = 6 }), draft.Revision, As(Author));

        Assert.Equal(draft.Revision + 1, edited.Revision);
        Assert.False(edited.HasSensitivityReport);
        Assert.Equal(RiskIndexPreviewState.Stale, (await Service.GetPreviewAsync(draft.Id, As(Author))).Status.State);
    }

    [Fact]
    public async Task TestOnlyADraftIsEditedPreviewedOrDeleted()
    {
        var submitted = await SubmittedAsync();

        var edit = await Refused(() => Service.UpdateAsync(submitted.Id,
            new RiskIndexProfileUpdateRequest("x", RiskIndexMode.Interim, submitted.Parameters), submitted.Revision, As(Author)));
        var preview = await Refused(() => Service.RequestPreviewAsync(submitted.Id, As(Author)));
        var delete = await Refused(() => Service.DeleteAsync(submitted.Id, As(Author)));

        Assert.All(new[] { edit, preview, delete }, r => Assert.Equal((409, RiskIndexErrors.InvalidTransition), (r.Status, r.Code)));
    }

    [Fact]
    public async Task TestDeletingADraftTakesItsPreviews()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await CompletePreviewAsync(draft.Id, Author);

        await Service.DeleteAsync(draft.Id, As(Author));

        await using var db = OpenContext();
        Assert.Empty(db.RiskIndexProfiles);
        Assert.Empty(db.RiskIndexPreviewRequests);
    }

    [Fact]
    public async Task TestOnePreviewAtATimeAndItsReportStaysOffTheProfile()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await Service.RequestPreviewAsync(draft.Id, As(Author));

        var again = await Refused(() => Service.RequestPreviewAsync(draft.Id, As(Author)));
        Assert.Equal((409, RiskIndexErrors.PreviewInProgress), (again.Status, again.Code));

        await GetService<IRiskIndexPreviewService>().ProcessNextAsync(DateTime.UtcNow);
        var profile = await Service.GetAsync(draft.Id);
        Assert.Equal(draft.Revision, profile.Revision);
        Assert.False(profile.HasSensitivityReport);
        Assert.Equal(RiskIndexPreviewState.Completed, (await Service.GetPreviewAsync(draft.Id, As(Author))).Status.State);
    }

    // --- submission -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestSubmissionNeedsAJustificationAndAPreviewOfTheseParameters()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));

        var noJustification = await Refused(() => Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest(" "), As(Author)));
        var noPreview = await Refused(() => Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("why"), As(Author)));
        await CompletePreviewAsync(draft.Id, Author);
        await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1", RiskIndexMode.Interim,
            draft.Parameters with { P = 6 }), draft.Revision, As(Author));
        var stale = await Refused(() => Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("why"), As(Author)));

        Assert.Equal((400, RiskIndexErrors.JustificationRequired), (noJustification.Status, noJustification.Code));
        Assert.Equal((409, RiskIndexErrors.PreviewMissing), (noPreview.Status, noPreview.Code));
        Assert.Equal((409, RiskIndexErrors.PreviewStale), (stale.Status, stale.Code));
    }

    [Fact]
    public async Task TestAnInvalidDraftIsNotSubmitted()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1", RiskIndexMode.Interim,
            draft.Parameters with { Tau = 0.60, TauG = 0.50 }), draft.Revision, As(Author));
        await CompletePreviewAsync(draft.Id, Author);

        var refusal = await Refused(() => Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("why"), As(Author)));

        Assert.Equal((400, RiskIndexErrors.ProfileInvalid), (refusal.Status, refusal.Code));
        Assert.Contains((await Service.ValidateAsync(draft.Id)).Errors, e => e.Code == "tail_lock");
    }

    [Fact]
    public async Task TestSubmissionAttachesThePreview()
    {
        var submitted = await SubmittedAsync();

        Assert.Equal(RiskIndexProfileStatus.PendingApproval, submitted.Status);
        Assert.True(submitted.HasSensitivityReport);
        Assert.NotNull(submitted.SubmittedAtUtc);
        Assert.Equal(RiskIndexPreviewState.Completed, (await Service.GetPreviewAsync(submitted.Id, As(Approver))).Status.State);
    }

    // --- the segregated approval (S40 §6.4, §11.4) ---------------------------------------------------------------------

    [Fact]
    public async Task TestTheApproverRoleApprovesAndTheProfileStartsAtTheNextSnapshot()
    {
        var submitted = await SubmittedAsync();

        await AttachMinutesAsync(submitted.Id);
        var eligibility = await Service.GetApprovalEligibilityAsync(submitted.Id, As(Approver));
        var approved = await Service.ApproveAsync(submitted.Id, Approval(), As(Approver));

        Assert.All(eligibility.Checks, c => Assert.True(c.Passed, c.Code.ToString()));
        Assert.True(eligibility.RequiresBoardRef);
        Assert.Equal(RiskIndexProfileStatus.Active, approved.Status);
        Assert.Equal($"role:{ManagerRole}", approved.ApprovalGrantSource);
        Assert.Equal(RiskIndexSnapshotService.SnapshotInstant(DateTime.UtcNow).AddDays(1), approved.EffectiveFrom);
        Assert.Equal("CRTI 2026-10, item 4", approved.CommitteeDecisionRef);
        await using var db = OpenContext();
        Assert.Contains(db.AuditLogs, a => a.EntityType == nameof(RiskIndexProfile) && a.Field == nameof(RiskIndexProfile.ApprovalGrantSource));
    }

    /// <summary>Negative case: the author approves the profile they wrote.</summary>
    [Fact]
    public async Task TestTheAuthorCannotApproveTheirOwnProfile()
    {
        var submitted = await SubmittedAsync(author: Approver);

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), As(Approver)));
        var eligibility = await Service.GetApprovalEligibilityAsync(submitted.Id, As(Approver));

        Assert.Equal((403, RiskIndexErrors.SodAuthorCannotApprove), (refusal.Status, refusal.Code));
        Assert.False(eligibility.Checks.Single(c => c.Code == RiskIndexApprovalCheckCode.SodAuthorCannotApprove).Passed);
    }

    /// <summary>An editor who is not the author is an author too: the audit trail names them.</summary>
    [Fact]
    public async Task TestAnEditorWhoIsNotTheAuthorCannotApprove()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1 (edited)", RiskIndexMode.Interim, draft.Parameters),
            draft.Revision, As(SecondApprover));
        await CompletePreviewAsync(draft.Id, Author);
        await Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("Pilot"), As(Author));

        var refusal = await Refused(() => Service.ApproveAsync(draft.Id, Approval(), As(SecondApprover)));

        Assert.Equal(RiskIndexErrors.SodAuthorCannotApprove, refusal.Code);
        await AttachMinutesAsync(draft.Id);
        Assert.Equal(RiskIndexProfileStatus.Active, (await Service.ApproveAsync(draft.Id, Approval(), As(Approver))).Status);
    }

    /// <summary>Negative case: the administrator flag passes every policy, but never approves weights (S40 §11.1).</summary>
    [Fact]
    public async Task TestAnAdministratorWithoutTheExplicitPermissionCannotApprove()
    {
        var submitted = await SubmittedAsync();
        var admin = As(AdminUser);
        Assert.True(admin.Has(RiskIndexPermissions.Approve)); // what the policy attribute sees

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), admin));
        var eligibility = await Service.GetApprovalEligibilityAsync(submitted.Id, admin);

        Assert.Equal((403, RiskIndexErrors.ApproverPermissionNotExplicit), (refusal.Status, refusal.Code));
        Assert.False(eligibility.Checks.Single(c => c.Code == RiskIndexApprovalCheckCode.ApproverPermissionNotExplicit).Passed);
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, (await Service.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task TestADirectGrantIsExplicitButDoesNotApprove()
    {
        var submitted = await SubmittedAsync();

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), As(DirectGrant)));
        var eligibility = await Service.GetApprovalEligibilityAsync(submitted.Id, As(DirectGrant));

        Assert.Equal((403, RiskIndexErrors.ApproverRoleRequired), (refusal.Status, refusal.Code));
        Assert.True(eligibility.Checks.Single(c => c.Code == RiskIndexApprovalCheckCode.ApproverPermissionNotExplicit).Passed);
        Assert.Null(eligibility.GrantSource);
    }

    [Fact]
    public async Task TestOnlyARoleListedInTheSettingApproves()
    {
        var submitted = await SubmittedAsync();

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), As(CommitteeMember)));
        Assert.Equal(RiskIndexErrors.ApproverRoleRequired, refusal.Code);

        await AttachMinutesAsync(submitted.Id, CommitteeMember);
        Seed(ctx => ctx.Settings.Single(s => s.Name == RiskIndexSettingKeys.ApproverRoles).Value = "Committee");
        var approved = await Service.ApproveAsync(submitted.Id, Approval(), As(CommitteeMember));
        Assert.Equal($"role:{CommitteeRole}", approved.ApprovalGrantSource);
    }

    [Theory]
    [InlineData(BusinessReviewer)]
    [InlineData(NamedReviewer)]
    public async Task TestABusinessReviewerCannotApprove(int reviewer)
    {
        var submitted = await SubmittedAsync();

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), As(reviewer)));

        Assert.Equal((403, RiskIndexErrors.SodBusinessReviewer), (refusal.Status, refusal.Code));
    }

    /// <summary>A scoped approver does not escape the reviewer check by not seeing the unit they review.</summary>
    [Fact]
    public async Task TestANamedReviewerOutsideTheApproversScopeIsStillRefused()
    {
        var submitted = await SubmittedAsync();
        _dal.Scope = EntityScope.ForEntities([999]);

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), As(NamedReviewer)));

        Assert.Equal(RiskIndexErrors.SodBusinessReviewer, refusal.Code);
    }

    /// <summary>Negative case: the committee's reference is required from the very first profile (S39 §13.1).</summary>
    [Fact]
    public async Task TestTheCommitteeReferenceIsRequired()
    {
        var submitted = await SubmittedAsync();

        var refusal = await Refused(() =>
            Service.ApproveAsync(submitted.Id, new RiskIndexApproveRequest("  ", "Board", false, null), As(Approver)));

        Assert.Equal((400, RiskIndexErrors.CommitteeRefRequired), (refusal.Status, refusal.Code));
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, (await Service.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task TestTheFirstProfileNeedsTheBoard()
    {
        var submitted = await SubmittedAsync();

        await AttachMinutesAsync(submitted.Id);
        var refusal = await Refused(() =>
            Service.ApproveAsync(submitted.Id, new RiskIndexApproveRequest("CRTI", null, false, null), As(Approver)));

        Assert.Equal((400, RiskIndexErrors.BoardRefRequired), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task TestOnlyAProfileAwaitingApprovalIsApproved()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));

        var refusal = await Refused(() => Service.ApproveAsync(draft.Id, Approval(), As(Approver)));
        var reject = await Refused(() => Service.RejectAsync(draft.Id, new RiskIndexRejectRequest("no"), As(Approver)));

        Assert.Equal((409, RiskIndexErrors.InvalidTransition), (refusal.Status, refusal.Code));
        Assert.Equal((409, RiskIndexErrors.InvalidTransition), (reject.Status, reject.Code));
    }

    /// <summary>A rebaseline (ω beyond ±0.05 in the quarter) is submitted, and approved only with the explicit confirmation.</summary>
    [Fact]
    public async Task TestARebaselineIsApprovedOnlyWithItsConfirmationAndRetiresThePrevious()
    {
        var first = await SubmittedAsync();
        await AttachMinutesAsync(first.Id);
        await Service.ApproveAsync(first.Id, Approval(), As(Approver));

        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v2", first.Id, null), As(Author));
        var parameters = draft.Parameters with
        {
            Omega = draft.Parameters.Omega with { Reg = 0.40, Exp = 0.20 }
        };
        await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v2", RiskIndexMode.Interim, parameters), draft.Revision,
            As(Author));
        await CompletePreviewAsync(draft.Id, Author);

        var shortReason = await Refused(() => Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("calibration"), As(Author)));
        Assert.Equal(RiskIndexErrors.RebaselineReasonRequired, shortReason.Code);
        await Service.SubmitAsync(draft.Id,
            new RiskIndexSubmitRequest("Calibration after the pilot moves REG and EXP beyond the quarter's pace."), As(Author));

        await AttachMinutesAsync(draft.Id, SecondApprover);
        var eligibility = await Service.GetApprovalEligibilityAsync(draft.Id, As(SecondApprover));
        Assert.Contains(RiskIndexRebaselineTrigger.OmegaPace, eligibility.RebaselineTriggers);
        var unconfirmed = await Refused(() =>
            Service.ApproveAsync(draft.Id, new RiskIndexApproveRequest("CRTI", null, false, null), As(SecondApprover)));
        Assert.Equal((400, RiskIndexErrors.RebaselineConfirmationRequired), (unconfirmed.Status, unconfirmed.Code));

        var approved = await Service.ApproveAsync(draft.Id, new RiskIndexApproveRequest("CRTI", null, true, null), As(SecondApprover));

        Assert.True(approved.IsRebaseline);
        var previous = await Service.GetAsync(first.Id);
        Assert.Equal((RiskIndexProfileStatus.Retired, approved.EffectiveFrom), (previous.Status, previous.RetiredAt));
    }

    // --- rejection and suspension -------------------------------------------------------------------------------------

    [Fact]
    public async Task TestARejectionGivesItsReason()
    {
        var submitted = await SubmittedAsync();

        var noReason = await Refused(() => Service.RejectAsync(submitted.Id, new RiskIndexRejectRequest(""), As(Approver)));
        var rejected = await Service.RejectAsync(submitted.Id, new RiskIndexRejectRequest("Weights not discussed"), As(Approver));

        Assert.Equal((400, RiskIndexErrors.ReasonRequired), (noReason.Status, noReason.Code));
        Assert.Equal((RiskIndexProfileStatus.Rejected, "Weights not discussed"), (rejected.Status, rejected.RejectedReason));
    }

    /// <summary>The suspension keeps the activation's evidence and writes its own (S40 §6.4); the next snapshot has no profile.</summary>
    [Fact]
    public async Task TestASuspensionKeepsTheActivationEvidenceAndStopsTheSnapshots()
    {
        var submitted = await SubmittedAsync();
        await AttachMinutesAsync(submitted.Id);
        await Service.ApproveAsync(submitted.Id, Approval(), As(Approver));

        var noJustification = await Refused(() =>
            Service.RetireAsync(submitted.Id, new RiskIndexRetireRequest("", "CRTI 11"), As(Approver)));
        var noReference = await Refused(() =>
            Service.RetireAsync(submitted.Id, new RiskIndexRetireRequest("Defect in τ", " "), As(Approver)));
        var retired = await Service.RetireAsync(submitted.Id, new RiskIndexRetireRequest("Defect in τ", "CRTI 11"), As(Approver));

        Assert.Equal(RiskIndexErrors.JustificationRequired, noJustification.Code);
        Assert.Equal(RiskIndexErrors.CommitteeRefRequired, noReference.Code);
        Assert.Equal(RiskIndexProfileStatus.Retired, retired.Status);
        Assert.Equal(("Pilot profile for the shadow period.", "CRTI 2026-10, item 4"), (retired.Justification, retired.CommitteeDecisionRef));
        Assert.Equal(("Defect in τ", "CRTI 11", $"user {Approver}"), (retired.RetireJustification, retired.RetireDecisionRef, retired.RetiredBy));

        var again = await Refused(() => Service.RetireAsync(submitted.Id, new RiskIndexRetireRequest("x", "y"), As(Approver)));
        Assert.Equal(RiskIndexErrors.InvalidTransition, again.Code);
        await using var db = OpenContext();
        Assert.Null(await RiskIndexSnapshotService.ProfileInForceAsync(db, DateTime.UtcNow.AddDays(2)));
    }

    // --- what a profile produced: global scope and the shadow pilot (S40 §8.4) -----------------------------------------

    [Fact]
    public async Task TestAScopedReaderReadsNoPreviewBridgeOrHistory()
    {
        var submitted = await SubmittedAsync();
        _dal.Scope = EntityScope.ForEntities([500]);

        var preview = await Refused(() => Service.GetPreviewAsync(submitted.Id, As(Approver)));
        var bridge = await Refused(() => Service.GetBridgeAsync(submitted.Id, As(Approver)));
        var history = await Refused(() => Service.GetHistoryAsync(submitted.Id, 100, As(Approver)));

        Assert.All(new[] { preview, bridge, history },
            r => Assert.Equal((403, RiskIndexErrors.ProfileValuesRequireGlobalScope), (r.Status, r.Code)));
    }

    /// <summary>S39 §13.3: in the shadow pilot, the author reads their draft's preview; another configurer does not.</summary>
    [Fact]
    public async Task TestInShadowOnlyTheAuthorEditorsCommitteeAndAdminsReadThePreview()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await CompletePreviewAsync(draft.Id, Author);

        Assert.NotNull(await Service.GetPreviewAsync(draft.Id, As(Author)));
        var other = await Refused(() => Service.GetPreviewAsync(draft.Id, As(OtherAnalyst)));
        Assert.Equal((403, RiskIndexErrors.InShadow), (other.Status, other.Code));
        Assert.NotNull(await Service.GetPreviewAsync(draft.Id, As(Approver)));
        Assert.NotNull(await Service.GetPreviewAsync(draft.Id, As(AdminUser)));

        Seed(ctx => ctx.Settings.Add(new Setting { Name = RiskIndexSettingKeys.Publication, Value = "published" }));
        Assert.NotNull(await Service.GetPreviewAsync(draft.Id, As(OtherAnalyst)));
    }

    [Fact]
    public async Task TestAProfileWithoutABridgeIsNotFoundAndTheHistoryIsBounded()
    {
        var submitted = await SubmittedAsync();

        var bridge = await Refused(() => Service.GetBridgeAsync(submitted.Id, As(Approver)));
        var limit = await Refused(() => Service.GetHistoryAsync(submitted.Id, 0, As(Approver)));
        var history = await Service.GetHistoryAsync(submitted.Id, 100, As(Approver));

        Assert.Equal((404, RiskIndexErrors.BridgeNotFound), (bridge.Status, bridge.Code));
        Assert.Equal(RiskIndexErrors.InvalidLimit, limit.Code);
        Assert.Contains(history, a => a.Field == nameof(RiskIndexProfile.Status) && a.UserId == Author);
    }

    [Fact]
    public async Task TestTheConfigurationReadsAndTheDiff()
    {
        var first = await SubmittedAsync();
        var list = await Service.ListAsync();
        var diff = await Service.DiffAsync(first.Id, first.Id);

        Assert.Single(list);
        Assert.All(diff.Changes, c => Assert.False(c.Changed));
        Assert.Contains(Service.GetSchema().Items, i => i.Key == "desconto_aceite" && i.Locked);
        Assert.Contains(Service.GetPresets(), p => p.Key == RiskIndexPresets.PureMean && !p.Activatable);
        Assert.Null(await Service.GetActiveAsync());
        await Assert.ThrowsAsync<RiskIndexException>(() => Service.GetAsync(999));
    }

    [Fact]
    public async Task TestTheServiceRefusesACallerWithoutThePermission()
    {
        var viewer = new RiskIndexCaller(OtherAnalyst, false, new HashSet<string> { RiskIndexPermissions.View });

        var create = await Refused(() =>
            Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), viewer));
        var approve = await Refused(() => Service.ApproveAsync(1, Approval(), As(Author)));

        Assert.Equal(403, create.Status);
        Assert.Equal(403, approve.Status);
    }
}
