using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Read;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The M53 security review of the segregated approval (S40 §6.4, §11.4; acceptance criteria 27–28): the self-granted
/// approver, the authors recorded on the profile rather than in the purgeable audit trail, the committee's minutes and the
/// explicit grant the suspension needs. Each refusal is a negative case that passed before the fix.
/// </summary>
public partial class RiskIndexProfilesServiceTest
{
    private const int OtherAdmin = 31;

    private const string AdministratorListed = RiskIndexRoles.DefaultApproverRoles + "," + RiskIndexRoles.Administrator;

    /// <summary>A change made through the given user's own session: the DAL attributes the save to them.</summary>
    private async Task AsActorAsync(int actor, Func<Task> change)
    {
        _dal.ActingUserId = actor;
        await change();
    }

    private Task GrantApproveToRoleAsync(int actor, int role) => AsActorAsync(actor, () =>
        GetService<IRolesService>().UpdatePermissionsAsync(role, [RiskIndexPermissions.View, RiskIndexPermissions.Approve]));

    private Task ListApproverRolesAsync(int actor, string value) => AsActorAsync(actor, () =>
        GetService<ISettingsService>().SetConfigurationKeyValueAsync(RiskIndexSettingKeys.ApproverRoles, value));

    private Task MoveToRoleAsync(int actor, int user, int role) => AsActorAsync(actor, async () =>
    {
        await using var db = _dal.GetContext();
        (await db.Users.SingleAsync(u => u.Value == user)).RoleId = role;
        await db.SaveChangesAsync();
    });

    /// <summary>Approve as <paramref name="approver"/> with the minutes attached; returns the refusal, if any.</summary>
    private async Task<RiskIndexException?> TryApproveAsync(int profile, int approver)
    {
        await AttachMinutesAsync(profile, approver);
        try
        {
            await Service.ApproveAsync(profile, Approval(), As(approver));
            return null;
        }
        catch (RiskIndexException ex)
        {
            return ex;
        }
    }

    private async Task AssertSelfGrantedAsync(int profile, int approver)
    {
        var refusal = await TryApproveAsync(profile, approver);
        var eligibility = await Service.GetApprovalEligibilityAsync(profile, As(approver));

        Assert.NotNull(refusal);
        Assert.Equal((403, RiskIndexErrors.SodSelfGrantedApprover), (refusal!.Status, refusal.Code));
        Assert.All(eligibility.Checks.Where(c => c.Code != RiskIndexApprovalCheckCode.SodSelfGrantedApprover),
            c => Assert.True(c.Passed, c.Code.ToString()));
        Assert.False(eligibility.Checks.Single(c => c.Code == RiskIndexApprovalCheckCode.SodSelfGrantedApprover).Passed);
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, (await Service.GetAsync(profile)).Status);
    }

    // --- sod_self_granted_approver (finding 1) -------------------------------------------------------------------------

    /// <summary>The administrator grants their own role risk_index_approve (the role is listed by somebody else).</summary>
    [Fact]
    public async Task TestAnAdministratorWhoGrantsTheirOwnRoleApproveCannotApprove()
    {
        Seed(ctx => ctx.Settings.Single(s => s.Name == RiskIndexSettingKeys.ApproverRoles).Value = AdministratorListed);
        var submitted = await SubmittedAsync();

        await GrantApproveToRoleAsync(AdminUser, AdministratorRole);

        await AssertSelfGrantedAsync(submitted.Id, AdminUser);
    }

    /// <summary>The administrator moves themselves into the Risk Manager role, which somebody else gave the grant.</summary>
    [Fact]
    public async Task TestAnAdministratorWhoJoinsAnApproverRoleCannotApprove()
    {
        var submitted = await SubmittedAsync();

        await MoveToRoleAsync(AdminUser, AdminUser, ManagerRole);

        await AssertSelfGrantedAsync(submitted.Id, AdminUser);
    }

    /// <summary>The administrator lists their own role, granted by somebody else, in risk_index_approver_roles.</summary>
    [Fact]
    public async Task TestAnAdministratorWhoListsTheirOwnRoleAsApproverCannotApprove()
    {
        await GrantApproveToRoleAsync(OtherAdmin, AdministratorRole);
        var submitted = await SubmittedAsync();

        await ListApproverRolesAsync(AdminUser, AdministratorListed);

        await AssertSelfGrantedAsync(submitted.Id, AdminUser);
    }

    /// <summary>The setting lists names: renaming one's own role into the list is the same as listing it.</summary>
    [Fact]
    public async Task TestAnAdministratorWhoRenamesTheirOwnRoleIntoTheSettingCannotApprove()
    {
        await GrantApproveToRoleAsync(OtherAdmin, AdministratorRole);
        await ListApproverRolesAsync(OtherAdmin, RiskIndexRoles.DefaultApproverRoles + ",Governors");
        var submitted = await SubmittedAsync();

        await AsActorAsync(AdminUser, async () =>
        {
            await using var db = _dal.GetContext();
            (await db.Roles.SingleAsync(r => r.Value == AdministratorRole)).Name = "Governors";
            await db.SaveChangesAsync();
        });

        await AssertSelfGrantedAsync(submitted.Id, AdminUser);
    }

    /// <summary>The positive case: another administrator granted, listed and moved — the approver authored none of it.</summary>
    [Fact]
    public async Task TestAGrantByAnotherAdministratorApproves()
    {
        await GrantApproveToRoleAsync(OtherAdmin, AdministratorRole);
        await ListApproverRolesAsync(OtherAdmin, AdministratorListed);
        await MoveToRoleAsync(OtherAdmin, AdminUser, PlainRole);
        await MoveToRoleAsync(OtherAdmin, AdminUser, AdministratorRole);
        var submitted = await SubmittedAsync();

        Assert.Null(await TryApproveAsync(submitted.Id, AdminUser));

        var approved = await Service.GetAsync(submitted.Id);
        Assert.Equal((RiskIndexProfileStatus.Active, $"role:{AdministratorRole}"), (approved.Status, approved.ApprovalGrantSource));
    }

    /// <summary>A self-grant later re-granted by somebody else is no longer the grant in force.</summary>
    [Fact]
    public async Task TestOnlyTheGrantInForceCounts()
    {
        Seed(ctx => ctx.Settings.Single(s => s.Name == RiskIndexSettingKeys.ApproverRoles).Value = AdministratorListed);
        await GrantApproveToRoleAsync(AdminUser, AdministratorRole);
        await AsActorAsync(OtherAdmin, () => GetService<IRolesService>().UpdatePermissionsAsync(AdministratorRole, []));
        await GrantApproveToRoleAsync(OtherAdmin, AdministratorRole);
        var submitted = await SubmittedAsync();

        Assert.Null(await TryApproveAsync(submitted.Id, AdminUser));
    }

    /// <summary>
    /// The grant record is not the audit trail: the audit retention purges <c>audit_logs</c>, and the self-grant is still
    /// refused — the record of who granted it lives in <c>permission_grant_events</c>, which the retention never touches.
    /// </summary>
    [Fact]
    public async Task TestTheSelfGrantIsRememberedPastTheAuditRetention()
    {
        Seed(ctx => ctx.Settings.Single(s => s.Name == RiskIndexSettingKeys.ApproverRoles).Value = AdministratorListed);
        var submitted = await SubmittedAsync();
        await GrantApproveToRoleAsync(AdminUser, AdministratorRole);

        Assert.True(await GetService<IAuditTrailService>().ApplyRetentionAsync(DateTime.UtcNow.AddYears(50)) > 0);

        await using (var db = OpenContext())
        {
            Assert.Empty(db.AuditLogs);
            Assert.Contains(db.PermissionGrantEvents, e => e.Kind == PermissionGrantKind.RolePermission &&
                                                         e.RoleId == AdministratorRole && e.ActorId == AdminUser &&
                                                         e.PermissionKey == RiskIndexPermissions.Approve);
        }

        await AssertSelfGrantedAsync(submitted.Id, AdminUser);
    }

    // --- the authors, from the profile's own record (findings 2 and 4) --------------------------------------------------

    /// <summary>Finding 2: an editor is still an author once the retention has purged the audit trail that named them.</summary>
    [Fact]
    public async Task TestAnEditorCannotApproveAfterTheAuditRetentionPurgedTheTrail()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1 (edited)", RiskIndexMode.Interim, draft.Parameters),
            draft.Revision, As(SecondApprover));
        await CompletePreviewAsync(draft.Id, Author);
        await Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("Pilot"), As(Author));

        await GetService<IAuditTrailService>().ApplyRetentionAsync(DateTime.UtcNow.AddYears(50));
        await using (var db = OpenContext())
            Assert.Empty(db.AuditLogs);

        var refusal = await TryApproveAsync(draft.Id, SecondApprover);

        Assert.Equal((403, RiskIndexErrors.SodAuthorCannotApprove), (refusal?.Status, refusal?.Code));
        Assert.Null(await TryApproveAsync(draft.Id, Approver));
    }

    /// <summary>Finding 4: a third user who only submitted somebody else's draft wrote its justification — an author.</summary>
    [Fact]
    public async Task TestAUserWhoOnlySubmittedCannotApprove()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await CompletePreviewAsync(draft.Id, Author);
        await Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("Pilot, submitted for the analyst."), As(Approver));

        var refusal = await TryApproveAsync(draft.Id, Approver);
        await GetService<IAuditTrailService>().ApplyRetentionAsync(DateTime.UtcNow.AddYears(50));
        var afterRetention = await TryApproveAsync(draft.Id, Approver);

        Assert.Equal((403, RiskIndexErrors.SodAuthorCannotApprove), (refusal?.Status, refusal?.Code));
        Assert.Equal(RiskIndexErrors.SodAuthorCannotApprove, afterRetention?.Code);
        await using (var db = OpenContext())
            Assert.Equal(Approver, db.RiskIndexProfiles.Single(p => p.Id == draft.Id).SubmittedById);
        Assert.Null(await TryApproveAsync(draft.Id, SecondApprover));
    }

    /// <summary>The shadow pilot's "authors read their draft" reads the same record: an editor reads the preview.</summary>
    [Fact]
    public async Task TestInShadowAnEditorReadsTheDraftsPreviewAfterTheAuditRetention()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        await Service.UpdateAsync(draft.Id, new RiskIndexProfileUpdateRequest("v1", RiskIndexMode.Interim, draft.Parameters),
            draft.Revision, As(OtherAnalyst));
        await CompletePreviewAsync(draft.Id, Author);
        await GetService<IAuditTrailService>().ApplyRetentionAsync(DateTime.UtcNow.AddYears(50));

        Assert.NotNull(await Service.GetPreviewAsync(draft.Id, As(OtherAnalyst)));
    }

    // --- the committee's minutes (finding 3, criterion 28) --------------------------------------------------------------

    [Fact]
    public async Task TestTheApprovalNeedsTheCommitteeMinutesAttached()
    {
        var submitted = await SubmittedAsync();

        var refusal = await Refused(() => Service.ApproveAsync(submitted.Id, Approval(), As(Approver)));

        Assert.Equal((400, RiskIndexErrors.CommitteeAttachmentRequired), (refusal.Status, refusal.Code));
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, (await Service.GetAsync(submitted.Id)).Status);
        Assert.Empty(await Service.ListAttachmentsAsync(submitted.Id));
    }

    /// <summary>Only the caller's own upload, hanging off nothing else, while the profile awaits approval (S40 §8.4, §11.3).</summary>
    [Fact]
    public async Task TestOnlyTheApproversOwnUnlinkedFileIsAttachedToAPendingProfile()
    {
        var draft = await Service.CreateAsync(new RiskIndexProfileCreateRequest("v1", null, RiskIndexPresets.Balanced), As(Author));
        var early = await Refused(() => Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(Upload(Approver)), As(Approver)));
        await CompletePreviewAsync(draft.Id, Author);
        await Service.SubmitAsync(draft.Id, new RiskIndexSubmitRequest("Pilot"), As(Author));

        var notOwned = await Refused(() => Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(Upload(Author)), As(Approver)));
        Seed(ctx => ctx.Risks.Add(new Risk
        {
            Id = 1, Status = "New", Subject = "R", ReferenceId = "R-1", Assessment = "", Notes = "", RiskCatalogMapping = "",
            ThreatCatalogMapping = "", SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow
        }));
        var riskFile = Upload(Approver, riskId: 1);
        var relinked = await Refused(() => Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(riskFile), As(Approver)));
        var missing = await Refused(() => Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(99_999), As(Approver)));
        var adminFlag = await Refused(() => Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(Upload(AdminUser)), As(AdminUser)));

        Assert.Equal((409, RiskIndexErrors.InvalidTransition), (early.Status, early.Code));
        Assert.Equal((403, RiskIndexErrors.FileNotOwned), (notOwned.Status, notOwned.Code));
        Assert.Equal((409, RiskIndexErrors.FileAlreadyAttached), (relinked.Status, relinked.Code));
        Assert.Equal((404, RiskIndexErrors.FileNotFound), (missing.Status, missing.Code));
        Assert.Equal((403, RiskIndexErrors.ApproverPermissionNotExplicit), (adminFlag.Status, adminFlag.Code));
        await using (var db = OpenContext())
            Assert.Null(db.NrFiles.Single(f => f.Id == riskFile).RiskIndexProfileId);

        var minutes = Upload(Approver);
        await Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(minutes), As(Approver));
        await Service.AttachAsync(draft.Id, new RiskIndexAttachRequest(minutes), As(Approver)); // the same link again is a no-op

        var listed = Assert.Single(await Service.ListAttachmentsAsync(draft.Id));
        Assert.Equal(("ata-" + minutes, Approver), (listed.UniqueName, listed.OwnerId));
    }

    /// <summary>
    /// <c>nr_files.risk_id</c> defaults to 0, so a real upload with no parent is stored with risk 0 (TestQ6 on MariaDB): that
    /// is no risk, and the link clears it, so the file then has exactly one parent — the profile.
    /// </summary>
    [Fact]
    public async Task TestAnUploadStoredWithTheLegacyRiskZeroIsAttached()
    {
        var submitted = await SubmittedAsync();
        var upload = Upload(Approver, riskId: 0);

        await Service.AttachAsync(submitted.Id, new RiskIndexAttachRequest(upload), As(Approver));

        await using var db = OpenContext();
        var file = db.NrFiles.Single(f => f.Id == upload);
        Assert.Equal(((int?)null, (int?)submitted.Id), (file.RiskId, file.RiskIndexProfileId));
    }

    // --- the suspension needs the explicit grant (finding 6) ------------------------------------------------------------

    [Fact]
    public async Task TestTheAdministratorFlagAloneDoesNotSuspend()
    {
        var submitted = await SubmittedAsync();
        await AttachMinutesAsync(submitted.Id);
        await Service.ApproveAsync(submitted.Id, Approval(), As(Approver));
        var admin = As(AdminUser);
        Assert.True(admin.Has(RiskIndexPermissions.Approve)); // what the policy attribute sees

        var refusal = await Refused(() => Service.RetireAsync(submitted.Id, new RiskIndexRetireRequest("Defect in τ", "CRTI 11"), admin));

        Assert.Equal((403, RiskIndexErrors.ApproverPermissionNotExplicit), (refusal.Status, refusal.Code));
        Assert.Equal(RiskIndexProfileStatus.Active, (await Service.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task TestTheAdministratorFlagAloneDoesNotReject()
    {
        var submitted = await SubmittedAsync();
        var admin = As(AdminUser);
        Assert.True(admin.Has(RiskIndexPermissions.Approve)); // what the policy attribute sees

        var refusal = await Refused(() => Service.RejectAsync(submitted.Id, new RiskIndexRejectRequest("Weights not discussed"), admin));

        Assert.Equal((403, RiskIndexErrors.ApproverPermissionNotExplicit), (refusal.Status, refusal.Code));
        Assert.Equal(RiskIndexProfileStatus.PendingApproval, (await Service.GetAsync(submitted.Id)).Status);
    }
}
