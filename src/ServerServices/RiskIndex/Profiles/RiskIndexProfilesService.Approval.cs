using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Read;

namespace ServerServices.RiskIndex.Profiles;

public sealed partial class RiskIndexProfilesService
{
    /// <summary>The permission whose holder must not approve the weights that measure their own unit (S39 §13.1).</summary>
    public const string BusinessRiskReview = "business_risk_review";

    /// <summary>The approval's verdict: each check and, when the approver role passes, the grant it rests on.</summary>
    internal sealed record Evaluation(IReadOnlyList<RiskIndexApprovalCheckDto> Checks, string? GrantSource)
    {
        public RiskIndexApprovalCheckDto? FirstFailure => Checks.FirstOrDefault(c => !c.Passed);
    }

    public async Task<RiskIndexApprovalEligibilityDto> GetApprovalEligibilityAsync(int id, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Approve);
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: false, ct);
        var evaluation = await EvaluateApprovalAsync(db, profile, caller.UserId, ct);
        var validation = await ValidateAsync(db, profile, ct);
        return new RiskIndexApprovalEligibilityDto(evaluation.Checks, evaluation.GrantSource, validation.RequiresBoardRef,
            validation.RebaselineTriggers);
    }

    /// <summary>
    /// The segregation of duties of S39 §13.1 and S40 §6.4, §11.4, evaluated once for <c>ApprovalEligibility</c> and
    /// <c>Approve</c> so the two can never disagree. Nothing here reads the token: the approver's permissions and role are
    /// read from the database, so the administrator flag — which passes every permission policy — approves nothing. Who
    /// wrote the draft and who granted the approver's permission come from records written with each change
    /// (<c>risk_index_profile_editors</c>, <c>permission_grant_events</c>), never from <c>audit_logs</c>, which retention purges.
    /// </summary>
    internal async Task<Evaluation> EvaluateApprovalAsync(NRDbContext db, RiskIndexProfile profile, int approverId,
        CancellationToken ct)
    {
        var checks = new List<RiskIndexApprovalCheckDto>();

        checks.Add(new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.InvalidTransition,
            profile.Status == RiskIndexProfileStatus.PendingApproval, "The profile must be awaiting approval."));

        // Whoever created, edited or submitted the draft.
        var authored = (await RiskIndexProfileAuthors.ReadAsync(db, profile, ct)).Contains(approverId);
        checks.Add(new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.SodAuthorCannotApprove, !authored,
            "Whoever created or edited the draft cannot approve it."));

        var user = await db.Users.AsNoTracking().Include(u => u.Permissions).FirstOrDefaultAsync(u => u.Value == approverId, ct);
        var permissions = user is null ? [] : await permissionsService.GetUserPermissionsAsync(user);

        // A named business reviewer anywhere in the map: read by the approver's own id, outside their scope, because a
        // scoped approver must not pass this check by not seeing the unit they review.
        bool namedReviewer;
        await using (var unscoped = dalService.GetContext(withIdentity: false, bypassEntityScope: true))
            namedReviewer = await unscoped.EntityRiskReviewers.AsNoTracking().AnyAsync(r => r.UserId == approverId, ct);
        checks.Add(new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.SodBusinessReviewer,
            !permissions.Contains(BusinessRiskReview) && !namedReviewer,
            "Whoever holds business_risk_review does not approve the weights that measure their own unit."));

        checks.Add(new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.ApproverPermissionNotExplicit,
            permissions.Contains(RiskIndexPermissions.Approve),
            "risk_index_approve must be granted to you, not only implied by the administrator flag."));

        // The grant must come from an approver role (risk_index_approver_roles); a direct grant does not approve.
        string? source = null;
        Role? role = null;
        var approverRoles = await ApproverRolesAsync(db, ct);
        if (user is { RoleId: > 0 })
        {
            role = await db.Roles.AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Value == user.RoleId, ct);
            if (role is not null && approverRoles.Contains(role.Name) &&
                role.Permissions.Any(p => p.Key == RiskIndexPermissions.Approve))
                source = "role:" + role.Value.ToString(CultureInfo.InvariantCulture);
        }

        checks.Add(new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.ApproverRoleRequired, source is not null,
            "Only a role listed in risk_index_approver_roles (the Risk Manager or Risk Administrator) approves profiles."));

        var selfGranted = user is not null && await SelfGrantedAsync(db, user, role, approverRoles, ct);
        checks.Add(new RiskIndexApprovalCheckDto(RiskIndexApprovalCheckCode.SodSelfGrantedApprover, !selfGranted,
            "Your permission to approve was granted by you; ask another administrator."));
        return new Evaluation(checks, source);
    }

    /// <summary>
    /// <c>sod_self_granted_approver</c> (S40 §6.4, §11.4): the approver authored a change that puts their approval in force —
    /// the grant of <c>risk_index_approve</c> to their role or to them, their move into the role, or the edit of
    /// <c>risk_index_approver_roles</c> (or the naming of the role) that made the role an approver role. Each condition is
    /// attributed to its latest recorded change; one with no record (seeded, or older than the record) is nobody's.
    /// </summary>
    private static async Task<bool> SelfGrantedAsync(NRDbContext db, User user, Role? role, IReadOnlySet<string> approverRoles,
        CancellationToken ct)
    {
        var approver = user.Value;
        var events = db.PermissionGrantEvents.AsNoTracking();
        var authors = new List<int?>();

        if (role is not null)
        {
            if (role.Permissions.Any(p => p.Key == RiskIndexPermissions.Approve))
                authors.Add(await LatestGrantAuthorAsync(events.Where(e => e.Kind == PermissionGrantKind.RolePermission &&
                    e.RoleId == role.Value && e.PermissionKey == RiskIndexPermissions.Approve), ct));

            authors.Add(await LatestGrantAuthorAsync(events.Where(e => e.Kind == PermissionGrantKind.RoleMembership &&
                                                                     e.UserId == approver && e.RoleId == role.Value), ct));

            if (approverRoles.Contains(role.Name))
            {
                var naming = (await events
                        .Where(e => (e.Kind == PermissionGrantKind.ApproverRoles && e.RoleName == role.Name) ||
                                    (e.Kind == PermissionGrantKind.RoleName && e.RoleId == role.Value))
                        .OrderByDescending(e => e.Id)
                        .Select(e => new { e.Kind, e.Action, e.RoleName, e.NewValue, e.ActorId })
                        .ToListAsync(ct))
                    // The collation compares names without case; the setting does not.
                    .FirstOrDefault(e => string.Equals(e.Kind == PermissionGrantKind.ApproverRoles ? e.RoleName : e.NewValue,
                        role.Name, StringComparison.Ordinal));
                if (naming is { Action: PermissionGrantAction.Grant }) authors.Add(naming.ActorId);
            }
        }

        if (user.Permissions.Any(p => p.Key == RiskIndexPermissions.Approve))
            authors.Add(await LatestGrantAuthorAsync(events.Where(e => e.Kind == PermissionGrantKind.UserPermission &&
                                                                     e.UserId == approver &&
                                                                     e.PermissionKey == RiskIndexPermissions.Approve), ct));

        return authors.Contains(approver);
    }

    /// <summary>The author of the latest change of one condition, when that change put it in force.</summary>
    private static async Task<int?> LatestGrantAuthorAsync(IQueryable<PermissionGrantEvent> events, CancellationToken ct)
    {
        var latest = await events.OrderByDescending(e => e.Id).Select(e => new { e.Action, e.ActorId }).FirstOrDefaultAsync(ct);
        return latest is { Action: PermissionGrantAction.Grant } ? latest.ActorId : null;
    }

    private static async Task<IReadOnlySet<string>> ApproverRolesAsync(NRDbContext db, CancellationToken ct) =>
        DAL.Auditing.PermissionGrantRecorder.ApproverRoleNames(await db.Settings.AsNoTracking()
            .Where(s => s.Name == RiskIndexSettingKeys.ApproverRoles).Select(s => s.Value).FirstOrDefaultAsync(ct));

    private static RiskIndexException Refusal(RiskIndexApprovalCheckDto failed) => failed.Code switch
    {
        RiskIndexApprovalCheckCode.InvalidTransition => RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, failed.Message),
        RiskIndexApprovalCheckCode.SodAuthorCannotApprove => RiskIndexException.Forbidden(RiskIndexErrors.SodAuthorCannotApprove, failed.Message),
        RiskIndexApprovalCheckCode.SodBusinessReviewer => RiskIndexException.Forbidden(RiskIndexErrors.SodBusinessReviewer, failed.Message),
        RiskIndexApprovalCheckCode.ApproverPermissionNotExplicit =>
            RiskIndexException.Forbidden(RiskIndexErrors.ApproverPermissionNotExplicit, failed.Message),
        RiskIndexApprovalCheckCode.SodSelfGrantedApprover =>
            RiskIndexException.Forbidden(RiskIndexErrors.SodSelfGrantedApprover, failed.Message),
        _ => RiskIndexException.Forbidden(RiskIndexErrors.ApproverRoleRequired, failed.Message)
    };

    public async Task<RiskIndexProfileDto> ApproveAsync(int id, RiskIndexApproveRequest request, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Approve);
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: true, ct);

        var evaluation = await EvaluateApprovalAsync(db, profile, caller.UserId, ct);
        if (evaluation.FirstFailure is { } failed)
        {
            logger.Warning("Approval of risk index profile v{Version} refused to user {UserId}: {Code}", profile.Version,
                caller.UserId, failed.Code);
            throw Refusal(failed);
        }

        var committee = Reference(request.CommitteeDecisionRef);
        if (committee is null)
            throw RiskIndexException.BadRequest(RiskIndexErrors.CommitteeRefRequired,
                "Cite the IT Risk Committee's decision (minutes or date).");

        // The reference comes with the minutes themselves (S39 §13.1; S40 §6.4): read outside the approver's scope, because a
        // scoped approver must not fail — or pass — this on what their scope happens to show.
        bool minutes;
        await using (var unscoped = dalService.GetContext(withIdentity: false, bypassEntityScope: true))
            minutes = await unscoped.NrFiles.AsNoTracking().AnyAsync(f => f.RiskIndexProfileId == id, ct);
        if (!minutes)
            throw RiskIndexException.BadRequest(RiskIndexErrors.CommitteeAttachmentRequired,
                "Attach the IT Risk Committee's minutes before approving.");

        var validation = await ValidateAsync(db, profile, ct);
        if (!validation.IsValid)
            throw RiskIndexException.BadRequest(RiskIndexErrors.ProfileInvalid, "The profile does not pass the S39 §12.4 rules.",
                new Dictionary<string, object?> { ["errors"] = validation.Errors });
        var board = Reference(request.BoardApprovalRef);
        if (validation.RequiresBoardRef && board is null)
            throw RiskIndexException.BadRequest(RiskIndexErrors.BoardRefRequired,
                "The bands changed (or this is the first profile): cite the Board's approval.");
        if (validation.RequiresRebaseline && !request.ConfirmRebaseline)
            throw RiskIndexException.BadRequest(RiskIndexErrors.RebaselineConfirmationRequired,
                "This profile is a rebaseline (" + string.Join(", ", validation.RebaselineTriggers) + "): confirm it explicitly.",
                new Dictionary<string, object?> { ["triggers"] = validation.RebaselineTriggers });

        // In force from the next snapshot (S39 §13.1): the previous profile retires at that same instant (S40 §6.4).
        var now = DateTime.UtcNow;
        var effective = RiskIndexSnapshotService.SnapshotInstant(now).AddDays(1);
        var previous = await db.RiskIndexProfiles
            .Where(p => p.Id != id && p.Status == RiskIndexProfileStatus.Active && p.RetiredAt == null).ToListAsync(ct);
        foreach (var old in previous)
        {
            var oldRevision = old.Revision;
            old.Status = RiskIndexProfileStatus.Retired;
            old.RetiredAt = effective;
            old.RetiredById = caller.UserId;
            Touch(db, old, oldRevision);
        }

        var revision = profile.Revision;
        profile.Status = RiskIndexProfileStatus.Active;
        profile.ApprovedAt = now;
        profile.ApprovedById = caller.UserId;
        profile.ApprovalGrantSource = evaluation.GrantSource;
        profile.CommitteeDecisionRef = committee;
        profile.BoardApprovalRef = board;
        profile.IsRebaseline = validation.RequiresRebaseline;
        profile.EffectiveFrom = effective;
        Touch(db, profile, revision);
        await SaveAsync(db, Transition, ct);

        cache.Clear();
        logger.Information("Risk index profile v{Version} approved by user {UserId} ({Grant}), in force from {Effective:u}",
            profile.Version, caller.UserId, evaluation.GrantSource, effective);
        return await DtoAsync(db, profile, ct);
    }

    public async Task<RiskIndexProfileDto> RejectAsync(int id, RiskIndexRejectRequest request, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Approve);

        // A rejection is the committee's verdict too: the explicit grant, never the administrator flag alone.
        await RiskIndexExplicitPermission.RequireAsync(dalService, permissionsService, caller.UserId, RiskIndexPermissions.Approve, ct);
        var reason = Text(request.Reason) ?? throw RiskIndexException.BadRequest(RiskIndexErrors.ReasonRequired,
            "A rejection gives its reason.");

        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: true, ct);
        if (profile.Status != RiskIndexProfileStatus.PendingApproval)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "The profile is not awaiting approval.");

        var revision = profile.Revision;
        profile.Status = RiskIndexProfileStatus.Rejected;
        profile.RejectedReason = reason;
        Touch(db, profile, revision);
        await SaveAsync(db, Transition, ct);
        logger.Information("Risk index profile v{Version} rejected by user {UserId}", profile.Version, caller.UserId);
        return await DtoAsync(db, profile, ct);
    }

    /// <summary>
    /// The suspension (S40 §6.4): the active profile retires now, with no successor, under its own justification and the
    /// committee's reference — never overwriting <c>justification</c> or <c>committee_decision_ref</c>, which are the
    /// evidence of the activation. The snapshots stop until a new approval.
    /// </summary>
    public async Task<RiskIndexProfileDto> RetireAsync(int id, RiskIndexRetireRequest request, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        Require(caller, RiskIndexPermissions.Approve);

        // The emergency brake is the committee's (S40 §6.4): the explicit grant, never the administrator flag alone.
        await RiskIndexExplicitPermission.RequireAsync(dalService, permissionsService, caller.UserId, RiskIndexPermissions.Approve, ct);
        var justification = Text(request.Justification) ?? throw RiskIndexException.BadRequest(RiskIndexErrors.JustificationRequired,
            "A suspension gives its justification.");
        var committee = Reference(request.CommitteeDecisionRef) ?? throw RiskIndexException.BadRequest(
            RiskIndexErrors.CommitteeRefRequired, "Cite the committee's decision to suspend.");

        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: true, ct);
        if (profile.Status != RiskIndexProfileStatus.Active)
            throw RiskIndexException.Conflict(RiskIndexErrors.InvalidTransition, "Only the active profile is suspended.");

        var revision = profile.Revision;
        profile.Status = RiskIndexProfileStatus.Retired;
        profile.RetiredAt = DateTime.UtcNow;
        profile.RetiredById = caller.UserId;
        profile.RetireJustification = justification;
        profile.RetireDecisionRef = committee;
        Touch(db, profile, revision);
        await SaveAsync(db, Transition, ct);

        cache.Clear();
        logger.Warning("Risk index profile v{Version} suspended by user {UserId}", profile.Version, caller.UserId);
        return await DtoAsync(db, profile, ct);
    }

    private static string? Reference(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= MaxReferenceLength
            ? trimmed
            : throw RiskIndexException.BadRequest("reference_too_long", $"A reference is at most {MaxReferenceLength} characters.");
    }

    private static string? Text(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= MaxTextLength
            ? trimmed
            : throw RiskIndexException.BadRequest("text_too_long", $"The text is at most {MaxTextLength} characters.");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // What a profile produced: global scope, and the shadow pilot (S40 §8.4)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The preview, the bridge and the history carry the organization's values: global scope only
    /// (<c>profile_values_require_global_scope</c>). During the shadow pilot, also only the committee (explicit
    /// <c>risk_index_approve</c>), administrators, and the author or an editor of this very profile with
    /// <c>risk_index_configure</c> (S39 §13.3).
    /// </summary>
    private async Task EnsureMayReadValuesAsync(NRDbContext db, RiskIndexProfile profile, RiskIndexCaller caller,
        CancellationToken ct, bool shadowRule = true)
    {
        if (!dalService.GetCurrentEntityScope().IsUnrestricted)
            throw RiskIndexException.Forbidden(RiskIndexErrors.ProfileValuesRequireGlobalScope,
                "A profile's preview, bridge and history show the whole organization: they need global scope.");
        if (!shadowRule) return;
        if (await RiskIndexShadowRule.RefusalAsync(db, profile, caller.UserId, caller.IsAdmin, caller.HoldsExplicitly, ct) is { } refusal)
            throw refusal;
    }

    public async Task<RiskIndexBridgeDto> GetBridgeAsync(int id, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await using var db = dalService.GetContext();
        var profile = await FindAsync(db, id, tracked: false, ct);
        await EnsureMayReadValuesAsync(db, profile, caller, ct);

        var rows = await db.RiskIndexBridges.AsNoTracking().Where(b => b.ToProfileId == id).OrderBy(b => b.ScopeKind)
            .ThenBy(b => b.ScopeRef).ThenBy(b => b.Category).ToListAsync(ct);
        if (rows.Count == 0) throw RiskIndexException.NotFound(RiskIndexErrors.BridgeNotFound, "This profile has no bridge.");

        var from = await db.RiskIndexProfiles.AsNoTracking().Where(p => p.Id == rows[0].FromProfileId).Select(p => p.Version)
            .FirstOrDefaultAsync(ct);
        var entities = rows.Where(r => r.EntityId is not null).Select(r => r.EntityId!.Value).Distinct().ToList();
        var names = await RiskIndexLabels.EntityNamesAsync(db, entities, ct);
        var nodes = rows.Select(r => new RiskIndexBridgeNodeDto(NodeRef(r, names), r.Category, r.ValueOld, r.ValueNew,
            (RiskIndexStoredJson.Deserialize<List<StoredOverlayPoint>>(r.Overlay) ?? [])
            .Select(o => new RiskIndexOverlayPointDto(o.Date, o.Value)).ToList())).ToList();
        return new RiskIndexBridgeDto(from, profile.Version, rows[0].BridgeDate, nodes, rows.Any(r => r.OverlayPartial));
    }

    private static RiskIndexNodeRef NodeRef(RiskIndexBridge row, IReadOnlyDictionary<int, string> names) => row.ScopeKind switch
    {
        RiskIndexScopeKind.Entity when row.EntityId is { } id => new RiskIndexNodeRef(RiskIndexNodeKey.ForEntity(id).ToString(),
            names.GetValueOrDefault(id) ?? "#" + id.ToString(CultureInfo.InvariantCulture), "entity"),
        RiskIndexScopeKind.Organization => new RiskIndexNodeRef(RiskIndexNodeKey.Organization, "Organization",
            RiskIndexEntityTypesName),
        _ => new RiskIndexNodeRef(row.ScopeKind + ":" + row.ScopeRef, row.ScopeRef, row.ScopeKind.ToString())
    };

    private const string RiskIndexEntityTypesName = "organization";

    public async Task<IReadOnlyList<AuditLog>> GetHistoryAsync(int id, int limit, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > MaxHistory)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidLimit, $"The limit is 1 to {MaxHistory}.");
        await using (var db = dalService.GetContext())
        {
            var profile = await FindAsync(db, id, tracked: false, ct);
            await EnsureMayReadValuesAsync(db, profile, caller, ct, shadowRule: false);
        }

        return await auditTrail.GetForRecordAsync(nameof(RiskIndexProfile), id, limit);
    }
}
