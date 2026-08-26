using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.3 — the approval workflow engine.
///
/// Three rules that were previously conventions:
///
/// <list type="number">
/// <item>A risk cannot reach a status the evidence does not support. <c>Mitigation Planned</c>
/// requires a mitigation row; <c>Mgmt Reviewed</c> requires a review; <c>Closed</c> requires either
/// a review that did not ask for another one, or a live acceptance. Before this, <c>SaveRisk</c>
/// persisted whatever status the client sent.</item>
/// <item>Nobody decides their own risk. The reviewer or acceptor must not be the risk's submitter,
/// owner or manager — <em>including</em> administrators, who previously bypassed every check.</item>
/// <item>An appetite, once configured, gates acceptance: above the dual-approval threshold a second
/// distinct top-band approver is required, and above the ceiling the acceptance is refused.</item>
/// </list>
/// </summary>
public class RiskWorkflowService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IRiskWorkflowService
{
    public const string StateMachineSetting = "risk_workflow_state_machine_enforced";
    public const string SegregationSetting = "risk_workflow_segregation_of_duties";
    public const string BreakGlassSetting = "risk_workflow_segregation_break_glass";

    // The register's status strings. Free text in the column, a closed set in practice; the
    // Track 6 `status_id` int is the type-safe replacement but the text column is still the source
    // of truth, so the machine is expressed over the strings the client actually sends.
    public const string StatusNew = "New";
    public const string StatusMitigationPlanned = "Mitigation Planned";
    public const string StatusManagementReview = "Mgmt Reviewed";
    public const string StatusClosed = "Closed";

    /// <summary>
    /// The next-step value that means "this review did not settle the risk". Seeded as
    /// <c>next_step</c> 1, "Request Risk review", and a risk whose latest review says that is not a
    /// risk anybody agreed to close.
    /// </summary>
    private const int NextStepRequestRiskReview = 1;

    public async Task EnsureTransitionAllowedAsync(int riskId, string fromStatus, string toStatus)
    {
        if (string.Equals(fromStatus, toStatus, StringComparison.OrdinalIgnoreCase)) return;
        if (!await IsEnabledAsync(StateMachineSetting, defaultValue: true)) return;

        await using var db = DalService.GetContext();

        var reason = await ViolationReasonAsync(db, riskId, toStatus);
        if (reason is null) return;

        Logger.Warning("Refused risk {RiskId} transition {From} → {To}: {Reason}", riskId, fromStatus,
            toStatus, reason);

        throw new InvalidStateTransitionException(fromStatus, toStatus, reason);
    }

    /// <summary>The reason a risk may not enter <paramref name="toStatus"/>, or null if it may.</summary>
    private static async Task<string?> ViolationReasonAsync(DAL.Context.AuditableContext db, int riskId,
        string toStatus)
    {
        switch (toStatus)
        {
            case StatusMitigationPlanned:
                var hasMitigation = await db.Mitigations.AnyAsync(m => m.RiskId == riskId);
                return hasMitigation
                    ? null
                    : "A risk cannot be marked 'Mitigation Planned' before a mitigation exists. Plan " +
                      "the mitigation first — the status is the record that a plan is in place, not a " +
                      "promise that one will be.";

            case StatusManagementReview:
                var hasReview = await db.MgmtReviews.AnyAsync(r => r.RiskId == riskId);
                return hasReview
                    ? null
                    : "A risk cannot be marked 'Mgmt Reviewed' with no management review on record.";

            case StatusClosed:
                var latest = await db.MgmtReviews
                    .Where(r => r.RiskId == riskId)
                    .OrderByDescending(r => r.SubmissionDate)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefaultAsync();

                if (latest is null)
                {
                    var accepted = await HasLiveAcceptanceAsync(db, riskId);
                    return accepted
                        ? null
                        : "A risk cannot be closed without a management review or a live risk " +
                          "acceptance. Closing is the organization's statement that it looked and " +
                          "decided; with neither record, there is nothing that statement rests on.";
                }

                if (latest.RequiresCountersignature && latest.SecondReviewerId is null)
                    return "The latest management review is waiting for a counter-signature, so the " +
                           "risk stays in review until a second approver signs it.";

                if (latest.NextStep == NextStepRequestRiskReview)
                    return "The latest management review asked for another review, so the risk is not " +
                           "settled and cannot be closed.";

                return null;

            default:
                return null;
        }
    }

    private static Task<bool> HasLiveAcceptanceAsync(DAL.Context.AuditableContext db, int riskId) =>
        db.RiskAcceptances.AnyAsync(a =>
            a.RiskId == riskId &&
            a.Status == DAL.Enums.RiskAcceptanceStatus.Active &&
            a.ExpiresAt > DateTime.UtcNow);

    public async Task EnsureSegregationOfDutiesAsync(int riskId, int actingUserId, string action,
        string? overrideReason = null)
    {
        if (!await IsEnabledAsync(SegregationSetting, defaultValue: true)) return;

        await using var db = DalService.GetContext();

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId);
        if (risk is null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));

        var conflicts = new List<string>();
        if (risk.SubmittedBy == actingUserId) conflicts.Add("submitted it");
        if (risk.Owner == actingUserId) conflicts.Add("own it");
        if (risk.Manager == actingUserId) conflicts.Add("manage it");

        if (conflicts.Count == 0) return;

        // "submitted it, own it and manage it" rather than a comma-joined list. The message is shown
        // verbatim to a business reviewer in the portal, and all three conflicts firing at once is the
        // common case, not the rare one.
        var relation = conflicts.Count switch
        {
            1 => conflicts[0],
            2 => $"{conflicts[0]} and {conflicts[1]}",
            _ => $"{string.Join(", ", conflicts.Take(conflicts.Count - 1))} and {conflicts[^1]}"
        };

        if (!string.IsNullOrWhiteSpace(overrideReason))
        {
            if (!await IsEnabledAsync(BreakGlassSetting, defaultValue: false))
                throw new PermissionInvalidException("risk_workflow_segregation_break_glass", actingUserId,
                    action);

            // Loud on purpose. A break-glass that leaves an ordinary-looking log line is
            // indistinguishable from a bypass, and the reason is also written onto the review row.
            Logger.Warning(
                "BREAK-GLASS: user {User} performed '{Action}' on risk {RiskId} which they {Relation}. " +
                "Stated reason: {Reason}", actingUserId, action, riskId, relation, overrideReason);
            return;
        }

        Logger.Warning("Refused '{Action}' on risk {RiskId} by user {User}, who {Relation}", action,
            riskId, actingUserId, relation);

        throw new RuleBrokenException(
            $"You cannot {action} this risk because you {relation}. A risk decision has to come from " +
            "someone other than the person who raised, owns or manages it — that separation is what " +
            "makes the approval mean anything.",
            "segregation_of_duties");
    }

    public async Task<AppetiteEvaluation> EvaluateAppetiteAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId);
        if (risk is null)
            throw new DataNotFoundException("local", "risks", new Exception($"Risk with id {riskId} not found"));

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);

        // Residual is the routing key. Falling back to the inherent score when residual has never
        // been computed is deliberate: gating on a null would mean an untreated risk sails past a
        // ceiling that a treated one is held to.
        double? residual = scoring?.ResidualRisk ?? scoring?.CalculatedRisk;

        var appetite = await ResolveAppetiteAsync(db, risk.EntityId);

        if (appetite is null)
            return new AppetiteEvaluation
            {
                AppetiteConfigured = false,
                ResidualScore = residual,
                Explanation = "No risk appetite is configured, so no acceptance is gated. Define one in " +
                              "Administration → Risk appetite to make the ceiling and the dual-approval " +
                              "threshold take effect."
            };

        var exceedsCeiling = residual is not null && residual > appetite.MaxAcceptableResidual;
        var requiresDual = residual is not null && residual > appetite.DualApprovalThreshold;

        return new AppetiteEvaluation
        {
            AppetiteConfigured = true,
            AppetiteId = appetite.Id,
            EntityId = appetite.EntityId,
            MaxAcceptableResidual = appetite.MaxAcceptableResidual,
            DualApprovalThreshold = appetite.DualApprovalThreshold,
            ResidualScore = residual,
            ExceedsCeiling = exceedsCeiling,
            RequiresDualApproval = requiresDual && !exceedsCeiling,
            Explanation = exceedsCeiling
                ? $"Residual {residual:F2} is above the acceptance ceiling of " +
                  $"{appetite.MaxAcceptableResidual:F2}. This risk cannot be accepted as it stands — it " +
                  "has to be mitigated further, or the appetite has to be raised, which is itself an " +
                  "audited decision."
                : requiresDual
                    ? $"Residual {residual:F2} is above the dual-approval threshold of " +
                      $"{appetite.DualApprovalThreshold:F2}, so this decision needs a second approver " +
                      "holding the top review band."
                    : $"Residual {residual:F2} is within appetite."
        };
    }

    /// <summary>
    /// The appetite that governs an entity: its own row, else the organization-wide default.
    /// A missing global row means no gating at all, which is the seeded state — an appetite invented
    /// during an upgrade would start refusing decisions the installation was making yesterday.
    /// </summary>
    private static async Task<RiskAppetite?> ResolveAppetiteAsync(DAL.Context.AuditableContext db,
        int? entityId)
    {
        if (entityId is not null)
        {
            var specific = await db.RiskAppetites.FirstOrDefaultAsync(a => a.EntityId == entityId);
            if (specific is not null) return specific;
        }

        return await db.RiskAppetites.FirstOrDefaultAsync(a => a.EntityId == null);
    }

    public async Task<List<AppetiteBreachCount>> CountRisksAboveAppetiteAsync()
    {
        await using var db = DalService.GetContext();

        var appetites = await db.RiskAppetites.ToListAsync();
        if (appetites.Count == 0) return [];

        var global = appetites.FirstOrDefault(a => a.EntityId == null);
        var byEntity = appetites.Where(a => a.EntityId != null)
            .ToDictionary(a => a.EntityId!.Value, a => a);

        var rows = await db.Risks
            .Where(r => r.Status != StatusClosed)
            .Join(db.RiskScorings, r => r.Id, s => s.Id,
                (r, s) => new { r.EntityId, s.ResidualRisk, s.CalculatedRisk })
            .ToListAsync();

        var counts = new Dictionary<int, int>();
        var globalCount = 0;

        foreach (var row in rows)
        {
            var ceiling = row.EntityId is not null && byEntity.TryGetValue(row.EntityId.Value, out var a)
                ? a.MaxAcceptableResidual
                : global?.MaxAcceptableResidual;

            if (ceiling is null) continue;

            // Residual where it exists, inherent where it does not: an untreated risk must not
            // escape a ceiling that a treated one is measured against.
            double score = row.ResidualRisk ?? row.CalculatedRisk;
            if (score <= ceiling) continue;

            if (row.EntityId is null) { globalCount++; continue; }

            counts.TryGetValue(row.EntityId.Value, out var current);
            counts[row.EntityId.Value] = current + 1;
        }

        // An entity's display name is a row in entities_properties, not a column on entities —
        // the register is schema-defined per installation.
        var ids = counts.Keys.ToList();
        var names = await db.EntitiesProperties
            .Where(p => p.Type == "name" && ids.Contains(p.Entity))
            .Select(p => new { p.Entity, p.Value })
            .ToDictionaryAsync(p => p.Entity, p => p.Value);

        var result = counts
            .Select(kv => new AppetiteBreachCount
            {
                EntityId = kv.Key,
                EntityName = names.TryGetValue(kv.Key, out var name) ? name : null,
                Count = kv.Value
            })
            .OrderByDescending(c => c.Count)
            .ToList();

        if (globalCount > 0)
            result.Insert(0, new AppetiteBreachCount { EntityId = null, EntityName = null, Count = globalCount });

        return result;
    }

    public async Task<List<WorkflowViolation>> FindLegacyViolationsAsync()
    {
        await using var db = DalService.GetContext();

        var violations = new List<WorkflowViolation>();

        var risks = await db.Risks
            .Where(r => r.Status == StatusMitigationPlanned || r.Status == StatusManagementReview ||
                        r.Status == StatusClosed)
            .Select(r => new { r.Id, r.Subject, r.Status })
            .ToListAsync();

        foreach (var risk in risks)
        {
            var reason = await ViolationReasonAsync(db, risk.Id, risk.Status);
            if (reason is null) continue;

            violations.Add(new WorkflowViolation
            {
                RiskId = risk.Id,
                Subject = risk.Subject,
                Status = risk.Status,
                Reason = reason
            });
        }

        return violations;
    }

    /// <summary>
    /// Reads a boolean switch out of <c>settings</c>. A missing row takes the default rather than
    /// failing: an installation upgraded before the seed lands must not lose the control.
    /// </summary>
    private async Task<bool> IsEnabledAsync(string key, bool defaultValue)
    {
        await using var db = DalService.GetContext();
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == key);
        if (setting?.Value is null) return defaultValue;

        return setting.Value.Trim().ToLowerInvariant() is "true" or "1" or "yes";
    }
}
