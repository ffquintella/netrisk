using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
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

        // Order matters. Segregation of duties is checked before authority: telling someone they
        // lack a permission when the real problem is that it is their own risk sends them to ask for
        // the permission, which is the wrong fix.
        await workflow.EnsureSegregationOfDutiesAsync(riskId, authorizerId, "accept",
            request.SegregationOverrideReason);

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        var residual = scoring?.ResidualRisk ?? scoring?.CalculatedRisk;

        await EnsureBandAuthorityAsync(db, authorizerId, residual);

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);
        if (appetite.ExceedsCeiling)
            throw new RuleBrokenException(appetite.Explanation, "risk_appetite_ceiling");

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

        var riskId = previous.RiskId.Value;
        var authorizerId = request.AuthorizingManagerId ?? actingUserId;

        await workflow.EnsureSegregationOfDutiesAsync(riskId, authorizerId, "renew the acceptance of",
            request.SegregationOverrideReason);

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        var residual = scoring?.ResidualRisk ?? scoring?.CalculatedRisk;

        await EnsureBandAuthorityAsync(db, authorizerId, residual);

        var appetite = await workflow.EvaluateAppetiteAsync(riskId);
        if (appetite.ExceedsCeiling)
            throw new RuleBrokenException(appetite.Explanation, "risk_appetite_ceiling");

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

        await db.SaveChangesAsync();

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

            // Largest threshold first, and only when it is smaller than the one already sent, so a
            // job that runs twice on a T-7 day warns once and a re-run of a failed pass is harmless.
            foreach (var threshold in WarningThresholds)
            {
                if (daysLeft > threshold) continue;
                if (acceptance.LastWarningDaysBefore is not null &&
                    acceptance.LastWarningDaysBefore <= threshold) break;

                acceptance.LastWarningDaysBefore = threshold;
                acceptance.UpdatedAt = asOfUtc;
                result.Warnings.Add((acceptance, threshold));
                break;
            }
        }

        await db.SaveChangesAsync();

        return result;
    }

    // --- internals ------------------------------------------------------------------------------

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
        if (levels.Count == 0 || score is null) return "insignificant";

        string band = "insignificant";
        foreach (var level in levels)
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
}
