using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Model.RiskFlags;
using Model.TailRisk;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.1 — risk acceptance as a first-class, expiring, authorized artifact.
///
/// Before this, "accepted" meant <c>PlanningStrategy = Accept</c> on the mitigation plus a
/// management review whose next step read "Accept until Next Review". There was no authorizing
/// manager, no justification field, no expiry date, and nothing that reopened the risk when the
/// acceptance lapsed. ISO 27001 clause 6.1.3 asks for the risk owner's formal, documented acceptance
/// of residual risk; that is what these rows are.
///
/// Every acceptance also writes a <c>MgmtReview</c>. The existing review history stays the single
/// approval timeline, so the desktop app, the portal and the auditor export all read one sequence
/// rather than three that have to be reconciled.
/// </summary>
public class RiskAcceptancesService(
    ILogger logger,
    IDalService dalService,
    IRiskWorkflowService workflow,
    IPermissionsService permissionsService)
    : ServiceBase(logger, dalService), IRiskAcceptancesService
{
    /// <summary>The pre-expiry warning thresholds the milestone names, largest first.</summary>
    public static readonly int[] WarningThresholds = [30, 7];

    /// <summary>
    /// The <c>next_step</c> value seeded as "Accept until Next Review". An acceptance's review row
    /// uses it so the existing GUI renders the timeline entry correctly without a new lookup value.
    /// </summary>
    private const int NextStepAcceptUntilNextReview = 3;

    /// <summary>The seeded <c>review</c> value for "Consider for Project" is 1; 2 is "Accept the risk".</summary>
    private const int ReviewAcceptTheRisk = 2;

    public async Task<List<RiskAcceptance>> GetByRiskAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        return await db.RiskAcceptances
            .Where(a => a.RiskId == riskId)
            .Include(a => a.AuthorizingManager)
            .Include(a => a.RequestedBy)
            .Include(a => a.RevokedBy)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync();
    }

    public async Task<RiskAcceptance?> GetActiveAsync(int riskId)
    {
        await using var db = DalService.GetContext();
        var now = DateTime.UtcNow;

        return await db.RiskAcceptances
            .Where(a => a.RiskId == riskId && a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > now)
            .Include(a => a.AuthorizingManager)
            .OrderByDescending(a => a.ExpiresAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<RiskAcceptance>> GetExpiringAsync(int days)
    {
        if (days < 0) throw new InvalidParameterException(nameof(days), "A negative window is not a window.");

        await using var db = DalService.GetContext();
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(days);

        return await db.RiskAcceptances
            .Where(a => a.RiskId != null && a.Status == RiskAcceptanceStatus.Active &&
                        a.ExpiresAt > now && a.ExpiresAt <= horizon)
            .Include(a => a.Risk)
            .Include(a => a.AuthorizingManager)
            .OrderBy(a => a.ExpiresAt)
            .ToListAsync();
    }

    public async Task<RiskAcceptance> CreateAsync(int riskId, RiskAcceptanceRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        Validate(request);

        await using var db = DalService.GetContext();

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("local", "risks",
                       new Exception($"Risk with id {riskId} not found"));

        if (await db.RiskAcceptances.AnyAsync(a =>
                a.RiskId == riskId && a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > DateTime.UtcNow))
            throw new DataAlreadyExistsException("local", "risk_acceptances", riskId.ToString(),
                "This risk already has a live acceptance. Renew or revoke it rather than stacking a " +
                "second one — two live acceptances mean nobody can say which decision is in force.");

        var authorizerId = request.AuthorizingManagerId ?? actingUserId;

        // Order matters. Gate A first (Stage 9.5, S46 §4.7, D7): it says nobody may accept this risk, so
        // it outranks every check about who is asking — and it precedes the appetite (Gate B) below, which
        // is the order the methodology gives the gates. Then segregation of duties before authority:
        // telling someone they lack a permission when the real problem is that it is their own risk sends
        // them to ask for the permission, which is the wrong fix.
        await workflow.EnsureGateAAllowsAsync(riskId, GateAAction.Accept);

        // Stage 9.9 (S50 §4.7): the third line gives assurance and never decides — not even when somebody else names an
        // auditor as the authorizing manager.
        await EnsureNeitherIsThirdLineAsync(db, authorizerId, actingUserId, "authorize a risk acceptance");

        await workflow.EnsureSegregationOfDutiesAsync(riskId, authorizerId, "accept",
            request.SegregationOverrideReason);

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        var residual = scoring?.ResidualRisk ?? scoring?.CalculatedRisk;

        await EnsureBandAuthorityAsync(db, authorizerId, residual);

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);
        if (appetite.ExceedsCeiling)
            throw new RuleBrokenException(appetite.Explanation, "risk_appetite_ceiling");

        // Stage 9.7 (S48 §4.7.2): Gate B on the tail, after the ordinal ceiling, which keeps its rule and its order.
        // Not assessable (no tail statistics) does not refuse — the ceiling above still governs (S48 D12).
        EnsureTailWithinTolerance(appetite);

        // Stage 9.8 (S49 §4.8): Gate B by indicator, last. A stale or unread KRI is not assessable and does not refuse;
        // a stale one whose last reading was beyond its tolerance does (S49 D4).
        EnsureIndicatorsWithinTolerance(appetite);

        var acceptance = new RiskAcceptance
        {
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"Acceptance of risk {risk.ReferenceId ?? riskId.ToString()}"
                : request.Name.Trim(),
            RiskId = riskId,
            BusinessJustification = request.BusinessJustification!.Trim(),
            AuthorizingManagerId = authorizerId,
            RequestedById = actingUserId,
            StartDate = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt!.Value,
            CompensatingControls = request.CompensatingControls,
            ResidualScoreSnapshot = residual,
            Status = RiskAcceptanceStatus.Active,
            EntityId = risk.EntityId,
            CreatedAt = DateTime.UtcNow,
            CreatedById = actingUserId
        };

        db.RiskAcceptances.Add(acceptance);

        WriteReview(db, riskId, authorizerId, acceptance, appetite, request.SegregationOverrideReason);

        await db.SaveChangesAsync();

        Logger.Information(
            "Risk {RiskId} accepted by user {Authorizer} until {Expiry:yyyy-MM-dd} (residual {Residual})",
            riskId, authorizerId, acceptance.ExpiresAt, residual);

        return acceptance;
    }

    public async Task<RiskAcceptance> RenewAsync(int acceptanceId, RiskAcceptanceRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        Validate(request);

        await using var db = DalService.GetContext();

        var previous = await db.RiskAcceptances.FirstOrDefaultAsync(a => a.Id == acceptanceId)
                       ?? throw new DataNotFoundException("local", "risk_acceptances",
                           new Exception($"Risk acceptance with id {acceptanceId} not found"));

        if (previous.RiskId is null)
            throw new InvalidParameterException(nameof(acceptanceId),
                "This acceptance covers findings rather than a risk; renew it through the findings API.");

        if (previous.Status == RiskAcceptanceStatus.Revoked)
            throw new InvalidStateTransitionException(previous.Status.ToString(),
                RiskAcceptanceStatus.Renewed.ToString(),
                "A revoked acceptance is not renewed, it is replaced. Create a new acceptance with its " +
                "own justification — the revocation was a decision, and renewing past it would hide it.");

        // A renewed acceptance was already replaced by its successor. Renewing it again would leave the successor
        // and the new row both Active for the same risk — nobody could say which decision is in force.
        if (previous.Status == RiskAcceptanceStatus.Renewed)
            throw new InvalidStateTransitionException(previous.Status.ToString(),
                RiskAcceptanceStatus.Renewed.ToString(),
                "This acceptance was already renewed; renew the acceptance that replaced it.");

        var riskId = previous.RiskId.Value;
        var authorizerId = request.AuthorizingManagerId ?? actingUserId;

        // Same rule as the committee path (S50 §4.6): renewing an acceptance (say an expired one) while another is
        // live would also leave two in force. The unique predecessor index below closes the race after this useful
        // early check; the losing writer is translated to the same domain conflict.
        if (await db.RiskAcceptances.AnyAsync(a => a.RiskId == riskId && a.Id != previous.Id &&
                                                 a.Status == RiskAcceptanceStatus.Active &&
                                                 a.ExpiresAt > DateTime.UtcNow))
            throw new DataAlreadyExistsException("local", "risk_acceptances", riskId.ToString(),
                "Another acceptance of this risk is live: renewing this one would leave two in force.");

        // Gate A before everything else, as on creation: a renewal is a new acceptance (S46 §4.7).
        await workflow.EnsureGateAAllowsAsync(riskId, GateAAction.RenewAcceptance);

        await EnsureNeitherIsThirdLineAsync(db, authorizerId, actingUserId, "authorize a risk acceptance");

        await workflow.EnsureSegregationOfDutiesAsync(riskId, authorizerId, "renew the acceptance of",
            request.SegregationOverrideReason);

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        var residual = scoring?.ResidualRisk ?? scoring?.CalculatedRisk;

        await EnsureBandAuthorityAsync(db, authorizerId, residual);

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);
        if (appetite.ExceedsCeiling)
            throw new RuleBrokenException(appetite.Explanation, "risk_appetite_ceiling");

        EnsureTailWithinTolerance(appetite);

        EnsureIndicatorsWithinTolerance(appetite);

        previous.Status = RiskAcceptanceStatus.Renewed;
        previous.UpdatedAt = DateTime.UtcNow;

        var renewal = new RiskAcceptance
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? previous.Name : request.Name.Trim(),
            RiskId = riskId,
            BusinessJustification = request.BusinessJustification!.Trim(),
            AuthorizingManagerId = authorizerId,
            RequestedById = actingUserId,
            StartDate = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt!.Value,
            CompensatingControls = request.CompensatingControls ?? previous.CompensatingControls,
            ResidualScoreSnapshot = residual,
            Status = RiskAcceptanceStatus.Active,
            EntityId = previous.EntityId,
            CreatedAt = DateTime.UtcNow,
            CreatedById = actingUserId,
            RenewedFromId = previous.Id
        };

        db.RiskAcceptances.Add(renewal);

        WriteReview(db, riskId, authorizerId, renewal, appetite, request.SegregationOverrideReason);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ContinuityService.Mentions(ex, "uq_ra_renewed_from_id"))
        {
            throw new DataAlreadyExistsException("local", "risk_acceptances", previous.Id.ToString(),
                "This acceptance was renewed by another request. Renew its successor instead.");
        }

        Logger.Information("Risk acceptance {Previous} renewed as {Renewal} until {Expiry:yyyy-MM-dd}",
            previous.Id, renewal.Id, renewal.ExpiresAt);

        return renewal;
    }

    public async Task<RiskAcceptance> RevokeAsync(int acceptanceId, string reason, int actingUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidParameterException(nameof(reason),
                "A revocation needs a reason. Withdrawing an acceptance is as consequential as granting " +
                "one, and an unexplained withdrawal is not auditable.");

        await using var db = DalService.GetContext();

        var acceptance = await db.RiskAcceptances.FirstOrDefaultAsync(a => a.Id == acceptanceId)
                         ?? throw new DataNotFoundException("local", "risk_acceptances",
                             new Exception($"Risk acceptance with id {acceptanceId} not found"));

        if (acceptance.Status is RiskAcceptanceStatus.Revoked)
            throw new InvalidStateTransitionException(acceptance.Status.ToString(),
                RiskAcceptanceStatus.Revoked.ToString(), "This acceptance is already revoked.");

        acceptance.Status = RiskAcceptanceStatus.Revoked;
        acceptance.RevokedAt = DateTime.UtcNow;
        acceptance.RevokedById = actingUserId;
        acceptance.RevocationReason = reason.Trim();
        acceptance.UpdatedAt = DateTime.UtcNow;

        // Revoking puts the risk back in front of somebody. Without this the risk keeps whatever
        // status the acceptance justified, which is the exact failure mode the milestone is about.
        if (acceptance.RiskId is not null)
        {
            var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == acceptance.RiskId.Value);
            if (risk is not null) FlagForReview(risk, "A risk acceptance was revoked.");
        }

        await db.SaveChangesAsync();

        Logger.Information("Risk acceptance {Id} revoked by user {User}", acceptanceId, actingUserId);

        return acceptance;
    }

    public async Task<RiskAcceptanceExpiryResult> ProcessExpiryAsync(DateTime asOfUtc)
    {
        var result = new RiskAcceptanceExpiryResult();

        await using var db = DalService.GetContext();

        // Risk-level acceptances only. The finding-level half of this table is processed by
        // FindingLifecycleService, and running both from here would expire each row twice.
        var live = await db.RiskAcceptances
            .Where(a => a.RiskId != null && a.Status == RiskAcceptanceStatus.Active)
            .Include(a => a.Risk)
            .ToListAsync();

        foreach (var acceptance in live)
        {
            if (acceptance.ExpiresAt <= asOfUtc)
            {
                acceptance.Status = RiskAcceptanceStatus.Expired;
                acceptance.UpdatedAt = asOfUtc;

                if (acceptance.Risk is not null)
                    FlagForReview(acceptance.Risk,
                        $"The risk acceptance '{acceptance.Name}' expired on " +
                        $"{acceptance.ExpiresAt:yyyy-MM-dd}.");

                result.Expired.Add(acceptance);
                continue;
            }

            var daysLeft = (int)System.Math.Floor((acceptance.ExpiresAt - asOfUtc).TotalDays);

            // The *tightest* applicable threshold, not the first one that matches. At five days left
            // both 30 and 7 apply and the useful message is the 7-day one; picking the first would
            // pick 30, see that 30 had already been sent, and stay silent for the rest of the run-up.
            var applicable = WarningThresholds
                .Where(threshold => daysLeft <= threshold)
                .DefaultIfEmpty(0)
                .Min();

            if (applicable <= 0) continue;

            // Only when it is tighter than the one already sent, so a job that runs daily warns once
            // per threshold and a re-run of a failed pass is harmless.
            if (acceptance.LastWarningDaysBefore is not null &&
                acceptance.LastWarningDaysBefore <= applicable) continue;

            acceptance.LastWarningDaysBefore = applicable;
            acceptance.UpdatedAt = asOfUtc;
            result.Warnings.Add((acceptance, applicable));
        }

        await db.SaveChangesAsync();

        return result;
    }

    public async Task<RiskAcceptance> StageCommitteeAcceptanceAsync(DAL.Context.AuditableContext db,
        CommitteeAcceptance approval)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(approval);

        Validate(new RiskAcceptanceRequest
            { BusinessJustification = approval.BusinessJustification, ExpiresAt = approval.ExpiresAt });

        var riskId = approval.RiskId;
        var now = DateTime.UtcNow;

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("local", "risks",
                       new Exception($"Risk with id {riskId} not found"));

        RiskAcceptance? previous = null;
        if (approval.RenewsAcceptanceId is { } renewsId)
        {
            previous = await db.RiskAcceptances.FirstOrDefaultAsync(a => a.Id == renewsId && a.RiskId == riskId)
                       ?? throw new DataNotFoundException("local", "risk_acceptances",
                           new Exception($"Risk acceptance with id {renewsId} not found on risk {riskId}"));

            // Stricter than the individual RenewAsync (S50 §4.6): a renewed acceptance was already replaced, and renewing
            // it again — or renewing one while another is live — would leave two acceptances in force.
            if (previous.Status is RiskAcceptanceStatus.Revoked or RiskAcceptanceStatus.Renewed)
                throw new InvalidStateTransitionException(previous.Status.ToString(),
                    RiskAcceptanceStatus.Renewed.ToString(),
                    previous.Status == RiskAcceptanceStatus.Revoked
                        ? "A revoked acceptance is not renewed, it is replaced. Submit a new acceptance with its own " +
                          "justification."
                        : "This acceptance was already renewed; renew the acceptance that replaced it.");

            if (await db.RiskAcceptances.AnyAsync(a => a.RiskId == riskId && a.Id != previous.Id &&
                                                     a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > now))
                throw new DataAlreadyExistsException("local", "risk_acceptances", riskId.ToString(),
                    "Another acceptance of this risk is live: renewing this one would leave two in force.");

            await workflow.EnsureGateAAllowsAsync(riskId, GateAAction.RenewAcceptance);
        }
        else
        {
            if (await db.RiskAcceptances.AnyAsync(a =>
                    a.RiskId == riskId && a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > now))
                throw new DataAlreadyExistsException("local", "risk_acceptances", riskId.ToString(),
                    "This risk already has a live acceptance. Renew or revoke it rather than stacking a second one.");

            await workflow.EnsureGateAAllowsAsync(riskId, GateAAction.Accept);
        }

        // No individual band here (S50 D8): the committee's distinct approvals are the authority. Gate B is unchanged —
        // a committee no more exceeds the appetite the board approved than a manager does.
        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        var residual = scoring?.ResidualRisk ?? scoring?.CalculatedRisk;

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);
        if (appetite.ExceedsCeiling)
            throw new RuleBrokenException(appetite.Explanation, "risk_appetite_ceiling");

        EnsureTailWithinTolerance(appetite);
        EnsureIndicatorsWithinTolerance(appetite);

        if (previous is not null)
        {
            previous.Status = RiskAcceptanceStatus.Renewed;
            previous.UpdatedAt = now;
        }

        var acceptance = new RiskAcceptance
        {
            Name = string.IsNullOrWhiteSpace(approval.Name)
                ? previous?.Name ?? $"Acceptance of risk {risk.ReferenceId ?? riskId.ToString()}"
                : approval.Name.Trim(),
            RiskId = riskId,
            BusinessJustification = approval.BusinessJustification.Trim(),
            AuthorizingManagerId = approval.DecidingMemberId,
            RequestedById = approval.RequestedById,
            StartDate = now,
            ExpiresAt = approval.ExpiresAt,
            CompensatingControls = approval.CompensatingControls ?? previous?.CompensatingControls,
            ResidualScoreSnapshot = residual,
            Status = RiskAcceptanceStatus.Active,
            EntityId = previous?.EntityId ?? risk.EntityId,
            CreatedAt = now,
            CreatedById = approval.DecidingMemberId,
            RenewedFromId = previous?.Id
        };

        db.RiskAcceptances.Add(acceptance);

        // The same single timeline as every other acceptance, naming the committee. Above the dual-approval threshold
        // the second signature is another approving member: the collegiate decision has at least two by construction.
        var dual = appetite.RequiresDualApproval && approval.SecondApproverId is not null;
        db.MgmtReviews.Add(new MgmtReview
        {
            RiskId = riskId,
            SubmissionDate = now,
            Review = ReviewAcceptTheRisk,
            Reviewer = approval.DecidingMemberId,
            NextStep = NextStepAcceptUntilNextReview,
            Comments = $"Risk accepted by the risk committee '{approval.CommitteeName}' (decision #{approval.DecisionId}: " +
                       $"{approval.Approvals} of {approval.RequiredApprovals} required approvals) until " +
                       $"{acceptance.ExpiresAt:yyyy-MM-dd}. " + acceptance.BusinessJustification,
            NextReview = DateOnly.FromDateTime(acceptance.ExpiresAt),
            RequiresCountersignature = appetite.RequiresDualApproval,
            SecondReviewerId = dual ? approval.SecondApproverId : null,
            SecondReviewAt = dual ? now : null
        });

        Logger.Information(
            "Risk {RiskId} accepted by committee '{Committee}' (decision {Decision}, {Approvals}/{Required}) until " +
            "{Expiry:yyyy-MM-dd} (residual {Residual})", riskId, approval.CommitteeName, approval.DecisionId,
            approval.Approvals, approval.RequiredApprovals, acceptance.ExpiresAt, residual);

        return acceptance;
    }

    // --- internals ------------------------------------------------------------------------------

    private static async Task EnsureNeitherIsThirdLineAsync(DAL.Context.AuditableContext db, int authorizerId,
        int actingUserId, string action)
    {
        await ThirdLineGuard.EnsureNotThirdLineAsync(db, authorizerId, action);
        if (actingUserId != authorizerId) await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, action);
    }

    private static void Validate(RiskAcceptanceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessJustification))
            throw new InvalidParameterException(nameof(request.BusinessJustification),
                "An acceptance needs a written business justification. It is the field an auditor reads, " +
                "and an acceptance without one records that somebody clicked a button, not that the " +
                "organization made a decision.");

        if (request.ExpiresAt is null)
            throw new InvalidParameterException(nameof(request.ExpiresAt),
                "An acceptance needs an expiry date. An acceptance with no expiry is the failure this " +
                "record exists to prevent: 'accepted' quietly becoming 'forgotten'.");

        if (request.ExpiresAt.Value <= DateTime.UtcNow)
            throw new InvalidParameterException(nameof(request.ExpiresAt),
                "The expiry date has to be in the future. An acceptance that has already lapsed accepts " +
                "nothing.");
    }

    /// <summary>
    /// The severity-band authority check (8.1.1): accepting a risk needs the <c>review_*</c>
    /// permission matching its residual band, exactly as reviewing it does.
    ///
    /// Bands follow the seeded <c>risk_levels</c> thresholds (Low ≥ 0, Medium ≥ 4, High ≥ 7,
    /// Very High ≥ 10.1) read from the database rather than hard-coded, because an installation is
    /// expected to retune them.
    /// </summary>
    private async Task EnsureBandAuthorityAsync(DAL.Context.AuditableContext db, int userId, double? residual)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId)
                   ?? throw new DataNotFoundException("local", "user",
                       new Exception($"User with id {userId} not found"));

        var band = await ResolveBandAsync(db, residual);
        var permission = $"review_{band.Replace(" ", string.Empty).ToLowerInvariant()}";

        var permissions = await permissionsService.GetUserPermissionsAsync(user);
        if (permissions.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase))) return;

        // Administrators are not exempt from segregation of duties (8.3.2) but they do hold every
        // permission, which is what the band check is about — a distinct question.
        if (user.Admin) return;

        throw new PermissionInvalidException(permission, userId, "accept risk");
    }

    /// <summary>The display name of the risk-level band a score falls in.</summary>
    private static async Task<string> ResolveBandAsync(DAL.Context.AuditableContext db, double? score)
    {
        var levels = await db.RiskLevels.OrderBy(l => l.Value).ToListAsync();
        return ResolveBand(levels, score);
    }

    /// <summary>
    /// The band a score falls in, given the configured levels.
    ///
    /// Public and static so it can be tested directly: <c>risk_levels</c> is a keyless entity, which
    /// the EF in-memory provider refuses to track, so a service-level test cannot seed the bands it
    /// wants to assert on. Splitting the pure part out is the alternative to leaving the mapping
    /// untested.
    ///
    /// Falls back to the lowest band when nothing is configured or the score is unknown. That is the
    /// conservative direction for a *permission* check — <c>review_insignificant</c> is the narrowest
    /// band, so an unresolvable score demands the permission most people have rather than none.
    /// </summary>
    public static string ResolveBand(IReadOnlyList<RiskLevel> levels, double? score)
    {
        if (levels.Count == 0 || score is null) return "insignificant";

        var band = "insignificant";
        foreach (var level in levels.OrderBy(l => l.Value))
        {
            if (score >= (double)level.Value) band = level.DisplayName;
            else break;
        }

        return band;
    }

    /// <summary>
    /// The management review every acceptance leaves behind, so one timeline covers desktop reviews,
    /// portal decisions and acceptances instead of three that need reconciling.
    /// </summary>
    private static void WriteReview(DAL.Context.AuditableContext db, int riskId, int reviewerId,
        RiskAcceptance acceptance, AppetiteEvaluation appetite, string? overrideReason)
    {
        var review = new MgmtReview
        {
            RiskId = riskId,
            SubmissionDate = DateTime.UtcNow,
            Review = ReviewAcceptTheRisk,
            Reviewer = reviewerId,
            NextStep = NextStepAcceptUntilNextReview,
            Comments = $"Risk accepted until {acceptance.ExpiresAt:yyyy-MM-dd}. " +
                       acceptance.BusinessJustification,
            NextReview = DateOnly.FromDateTime(acceptance.ExpiresAt),
            // Above the dual-approval threshold the review lands unsigned by the second approver,
            // which is what holds the risk in review until somebody counter-signs (8.3.4).
            RequiresCountersignature = appetite.RequiresDualApproval,
            SegregationOverrideReason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason
        };

        db.MgmtReviews.Add(review);
    }

    /// <summary>
    /// Marks a risk as needing a look. Used when an acceptance lapses or is revoked — the flag is
    /// what the 8.5.1 notification job and the risk list read, so "reopened" is observable rather
    /// than merely logged.
    /// </summary>
    private static void FlagForReview(Risk risk, string reason)
    {
        risk.ReviewRequested = true;
        risk.ReviewRequestedAt = DateTime.UtcNow;
        risk.ReviewRequestedReason = reason;
        risk.LastUpdate = DateTime.UtcNow;
    }

    /// <summary>The rule name of a refusal by Gate B on the tail (Stage 9.7, S48 §4.7.2).</summary>
    public const string TailToleranceRule = "risk_appetite_tail_tolerance";

    /// <summary>Refuses an acceptance whose tail exceeds the appetite's monetary tolerance (S48 §4.7.2).</summary>
    private void EnsureTailWithinTolerance(AppetiteEvaluation appetite)
    {
        if (appetite.Tail.State != TailAppetiteState.ExceedsTolerance) return;

        Logger.Warning("Refused acceptance: the tail exceeds the appetite's tolerance ({Explanation})",
            appetite.Tail.Explanation);

        throw new RuleBrokenException(appetite.Tail.Explanation, TailToleranceRule);
    }

    /// <summary>The rule name of a refusal by Gate B by indicator (Stage 9.8, S49 §4.8).</summary>
    public const string IndicatorToleranceRule = "risk_appetite_indicator_tolerance";

    /// <summary>Refuses an acceptance a linked KRI says is beyond tolerance (S49 §4.8).</summary>
    private void EnsureIndicatorsWithinTolerance(AppetiteEvaluation appetite)
    {
        if (appetite.Indicators.State != Model.Monitoring.IndicatorAppetiteState.ExceedsTolerance) return;

        Logger.Warning("Refused acceptance: a key risk indicator exceeds its tolerance ({Explanation})",
            appetite.Indicators.Explanation);

        throw new RuleBrokenException(appetite.Indicators.Explanation, IndicatorToleranceRule);
    }
}
