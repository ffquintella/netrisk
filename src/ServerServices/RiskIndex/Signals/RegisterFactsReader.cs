using System.Globalization;
using System.Text.RegularExpressions;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using ServerServices.Governance;

namespace ServerServices.RiskIndex.Signals;

/// <summary>
/// Reads <see cref="ScenarioFacts"/> for every open scenario (<c>risks.status &lt;&gt; 'Closed'</c>, S39 §3.3) from the
/// register, through the context it is given — so under the caller's entity scope. The snapshot job runs it with the
/// background principal's unrestricted scope (T237); nothing here opens a bypass.
/// </summary>
public static partial class RegisterFactsReader
{
    /// <summary>The campaign cadence the rank's validity counts in (S39 §6.5; <c>Data/81.sql:29</c>, default 3 months).</summary>
    public const int DefaultCampaignCadenceMonths = 3;

    public static async Task<IReadOnlyDictionary<int, ScenarioFacts>> ReadAsync(NRDbContext db, Model.RiskIndex.RiskIndexParameters p,
        DateTime now, CancellationToken ct = default)
    {
        var risks = await db.Risks.AsNoTracking()
            .Where(r => r.Status != "Closed")
            .Select(r => new
            {
                r.Id, r.SubmissionDate, r.SubmittedBy, r.Owner, r.Manager, r.EntityId, r.ReviewRequestedAt,
                r.ReviewRequestedReason, r.IncidentResponsePlanId
            })
            .ToListAsync(ct);
        if (risks.Count == 0) return new Dictionary<int, ScenarioFacts>();

        var ids = risks.Select(r => r.Id).ToList();

        var scorings = await db.RiskScorings.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.CalculatedRisk, s.ResidualRisk, s.ResidualUpdatedAt, s.ScoringMethod, s.ClassicImpact })
            .ToDictionaryAsync(s => s.Id, ct);

        var reviews = await db.MgmtReviews.AsNoTracking().Where(m => ids.Contains(m.RiskId))
            .Select(m => new
            {
                m.Id, m.RiskId, m.Reviewer, m.SubmissionDate, m.RequiresCountersignature, m.SecondReviewerId
            })
            .ToListAsync(ct);

        var acceptances = await db.RiskAcceptances.AsNoTracking()
            .Where(a => a.RiskId != null && ids.Contains(a.RiskId.Value))
            .ToListAsync(ct);

        var items = await db.RiskReviewCampaignItems.AsNoTracking().Where(i => ids.Contains(i.RiskId))
            .Select(i => new
            {
                i.Id, i.RiskId, i.CampaignId, i.Rank, i.Decision, i.DecidedAt, i.DecidedById, i.CreatedAt,
                CampaignStatus = i.Campaign!.Status, CampaignDueDate = i.Campaign.DueDate,
                CampaignCreatedAt = i.Campaign.CreatedAt
            })
            .ToListAsync(ct);

        var tasks = await (from t in db.MitigationTasks.AsNoTracking()
                join m in db.Mitigations.AsNoTracking() on t.MitigationId equals m.Id
                where ids.Contains(m.RiskId)
                select new { m.RiskId, t.Id, t.Status, t.DueDate, t.CreatedAt, t.CompletedAt })
            .ToListAsync(ct);

        var appetites = await db.RiskAppetites.AsNoTracking()
            .Select(a => new { a.EntityId, a.MaxAcceptableResidual }).ToListAsync(ct);
        var globalCeiling = appetites.FirstOrDefault(a => a.EntityId == null)?.MaxAcceptableResidual;
        var ceilingByEntity = appetites.Where(a => a.EntityId != null)
            .GroupBy(a => a.EntityId!.Value).ToDictionary(g => g.Key, g => g.First().MaxAcceptableResidual);

        var planIds = risks.Where(r => r.IncidentResponsePlanId != null).Select(r => r.IncidentResponsePlanId!.Value)
            .Distinct().ToList();
        var plans = await db.IncidentResponsePlans.AsNoTracking().Where(i => planIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Status, i.HasBeenApproved, i.LastTestDate })
            .ToDictionaryAsync(i => i.Id, ct);

        var linkedHosts = (await db.Risks.AsNoTracking().Where(r => ids.Contains(r.Id))
                .SelectMany(r => r.Vulnerabilities, (r, v) => new { RiskId = r.Id, v.HostId })
                .Where(x => x.HostId != null)
                .ToListAsync(ct))
            .GroupBy(x => x.RiskId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.HostId!.Value).Distinct().OrderBy(h => h).ToList());

        var ranks = await RanksAsync(db, p, now, ct);
        var audit = await AuditAsync(db, ids, ct);

        var facts = new Dictionary<int, ScenarioFacts>(risks.Count);
        foreach (var risk in risks)
        {
            var scoring = scorings.GetValueOrDefault(risk.Id);
            var plan = risk.IncidentResponsePlanId is { } planId ? plans.GetValueOrDefault(planId) : null;
            double? ceiling = risk.EntityId is { } entity && ceilingByEntity.TryGetValue(entity, out var own)
                ? own
                : globalCeiling;

            var riskItems = items.Where(i => i.RiskId == risk.Id).ToList();
            var rankItem = riskItems.FirstOrDefault(i => risk.EntityId is { } e && ranks.TryGetValue(e, out var latest) &&
                                                         latest.CampaignId == i.CampaignId && i.Rank is >= 1);
            var rank = rankItem is not null && risk.EntityId is { } rankEntity
                ? new CampaignRankFact(rankItem.Rank!.Value, ranks[rankEntity].RankedItems, ranks[rankEntity].CompletedAt)
                : null;

            var impact = scoring?.ClassicImpact is { } ci && ci >= 1 && ci <= 5 ? (int?)Math.Round(ci) : null;

            facts[risk.Id] = new ScenarioFacts
            {
                RiskId = risk.Id,
                SubmissionDate = risk.SubmissionDate,
                SubmittedBy = risk.SubmittedBy,
                Owner = risk.Owner,
                Manager = risk.Manager,
                EntityId = risk.EntityId,
                Inherent = scoring?.CalculatedRisk ?? 0,
                Residual = scoring?.ResidualRisk,
                ResidualUpdatedAt = scoring?.ResidualUpdatedAt,
                ScoringMethod = scoring?.ScoringMethod ?? 0,
                ImpactLevel = impact,
                ReviewRequestedAt = risk.ReviewRequestedAt,
                ReviewRequestedReason = risk.ReviewRequestedReason,
                ReviewRequestFromAcceptance = audit.RequestFromAcceptance.Contains(risk.Id) ||
                                              IsAcceptanceRequestReason(risk.ReviewRequestedReason),
                Reviews = reviews.Where(r => r.RiskId == risk.Id).Select(r =>
                {
                    var row = audit.ReviewCreation(r.Id, r.RiskId, r.Reviewer, r.SubmissionDate);
                    return new ReviewFact
                    {
                        Id = r.Id,
                        ReviewerId = r.Reviewer,
                        SubmissionDate = r.SubmissionDate,
                        AuditedAt = row?.OccurredAt,
                        CorrelationId = row?.CorrelationId,
                        SavedWithAcceptance = row?.CorrelationId is { } c1 && audit.AcceptanceCreations.Contains(c1),
                        SavedWithCampaignDecision = row?.CorrelationId is { } c2 && audit.CampaignDecisions.Contains(c2),
                        RequiresCountersignature = r.RequiresCountersignature,
                        SecondReviewerId = r.SecondReviewerId
                    };
                }).ToList(),
                Acceptances = acceptances.Where(a => a.RiskId == risk.Id).Select(a => new AcceptanceFact
                {
                    Id = a.Id, Status = a.Status, CreatedAt = a.CreatedAt, ExpiresAt = a.ExpiresAt, RevokedAt = a.RevokedAt,
                    RenewedFromId = a.RenewedFromId, ResidualScoreSnapshot = a.ResidualScoreSnapshot,
                    CreatedById = a.CreatedById, RequestedById = a.RequestedById,
                    AuthorizingManagerId = a.AuthorizingManagerId
                }).ToList(),
                CampaignItems = riskItems.Select(i => new CampaignItemFact
                {
                    Id = i.Id, CampaignId = i.CampaignId, CampaignStatus = i.CampaignStatus,
                    CampaignDueDate = i.CampaignDueDate, CampaignCreatedAt = i.CampaignCreatedAt, Decision = i.Decision,
                    DecidedAt = i.DecidedAt, DecidedById = i.DecidedById, CreatedAt = i.CreatedAt
                }).ToList(),
                Tasks = tasks.Where(t => t.RiskId == risk.Id).Select(t => new TaskFact
                {
                    Id = t.Id, Status = t.Status, DueDate = t.DueDate, CreatedAt = t.CreatedAt, CompletedAt = t.CompletedAt
                }).ToList(),
                AppetiteCeiling = ceiling,
                Rank = rank,
                ApprovedResponsePlan = plan is not null &&
                                       (plan.HasBeenApproved == true || plan.Status == (int)Model.IntStatus.Approved),
                ResponsePlanLastTest = plan?.LastTestDate,
                LinkedHostIds = linkedHosts.GetValueOrDefault(risk.Id) ?? []
            };
        }

        return facts;
    }

    private sealed record LatestCampaign(int CampaignId, DateTime CompletedAt, int RankedItems);

    /// <summary>
    /// The latest completed campaign of each entity, when it completed within <c>validade_rank_ciclos</c> campaign
    /// cadences (S39 §6.5) — its ranks weight the scenarios; an older one weights nothing.
    /// </summary>
    private static async Task<Dictionary<int, LatestCampaign>> RanksAsync(NRDbContext db,
        Model.RiskIndex.RiskIndexParameters p, DateTime now, CancellationToken ct)
    {
        var cadenceText = await db.Settings.AsNoTracking()
            .Where(s => s.Name == RiskReviewCampaignsService.CadenceMonthsSetting).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var cadence = int.TryParse(cadenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var months) && months > 0
            ? months
            : DefaultCampaignCadenceMonths;

        var completed = await db.RiskReviewCampaigns.AsNoTracking()
            .Where(c => c.Status == RiskReviewCampaignStatus.Completed && c.CompletedAt != null)
            .Select(c => new { c.Id, c.EntityId, CompletedAt = c.CompletedAt!.Value })
            .ToListAsync(ct);

        var latest = completed.GroupBy(c => c.EntityId)
            .Select(g => g.OrderByDescending(c => c.CompletedAt).ThenByDescending(c => c.Id).First())
            .ToList();

        var campaignIds = latest.Select(c => c.Id).ToList();
        var ranked = await db.RiskReviewCampaignItems.AsNoTracking()
            .Where(i => campaignIds.Contains(i.CampaignId) && i.Rank != null)
            .GroupBy(i => i.CampaignId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return latest.Where(c => c.CompletedAt.AddMonths(Math.Max(1, p.RankValidityCycles) * cadence) >= now)
            .ToDictionary(c => c.EntityId, c => new LatestCampaign(c.Id, c.CompletedAt, ranked.GetValueOrDefault(c.Id)));
    }

    private sealed record AuditRow(int EntityId, string? NewValue, DateTime OccurredAt, string? CorrelationId);

    private sealed class AuditFacts
    {
        public required List<AuditRow> ReviewCreations;
        public required HashSet<string> AcceptanceCreations;
        public required HashSet<string> CampaignDecisions;
        public required HashSet<int> RequestFromAcceptance;

        /// <summary>
        /// The creation row of a review. A created row is keyed by EF's temporary id in production (the interceptor runs
        /// before the insert), so the row is matched by its id when it is real and otherwise by its recorded RiskId,
        /// Reviewer and SubmissionDate.
        /// </summary>
        public AuditRow? ReviewCreation(int id, int riskId, int reviewer, DateTime submission) =>
            ReviewCreations.Where(r => r.EntityId == id ||
                                       r.EntityId <= 0 && Field(r.NewValue, "RiskId") == riskId.ToString(CultureInfo.InvariantCulture) &&
                                       Field(r.NewValue, "Reviewer") == reviewer.ToString(CultureInfo.InvariantCulture) &&
                                       DateTime.TryParse(Field(r.NewValue, "SubmissionDate"), CultureInfo.InvariantCulture,
                                           DateTimeStyles.RoundtripKind, out var at) &&
                                       Math.Abs((at - submission).TotalSeconds) <= 1)
                .OrderBy(r => r.OccurredAt).FirstOrDefault();
    }

    private static async Task<AuditFacts> AuditAsync(NRDbContext db, List<int> riskIds, CancellationToken ct)
    {
        var reviewCreations = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(MgmtReview) && a.Action == AuditLogAction.Create)
            .Select(a => new AuditRow(a.EntityId, a.NewValue, a.OccurredAt, a.CorrelationId))
            .ToListAsync(ct);

        var acceptanceCreations = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(RiskAcceptance) && a.Action == AuditLogAction.Create && a.CorrelationId != null)
            .Select(a => a.CorrelationId!).Distinct().ToListAsync(ct);

        var campaignDecisions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(RiskReviewCampaignItem) && a.Action == AuditLogAction.Update &&
                        a.Field == nameof(RiskReviewCampaignItem.Decision) && a.CorrelationId != null)
            .Select(a => a.CorrelationId!).Distinct().ToListAsync(ct);

        var acceptanceChanges = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == nameof(RiskAcceptance) && a.Action == AuditLogAction.Update &&
                            (a.Field == nameof(RiskAcceptance.Status) || a.Field == nameof(RiskAcceptance.RevokedAt)) &&
                            a.CorrelationId != null)
                .Select(a => a.CorrelationId!).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        // The latest write of each risk's ReviewRequestedAt, and whether an acceptance changed in the same save.
        var requests = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Risk) && a.Action == AuditLogAction.Update &&
                        a.Field == nameof(Risk.ReviewRequestedAt) && riskIds.Contains(a.EntityId))
            .Select(a => new { a.EntityId, a.OccurredAt, a.Id, a.CorrelationId })
            .ToListAsync(ct);
        var fromAcceptance = requests.GroupBy(r => r.EntityId)
            .Select(g => g.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id).First())
            .Where(r => r.CorrelationId is { } c && acceptanceChanges.Contains(c))
            .Select(r => r.EntityId)
            .ToHashSet();

        return new AuditFacts
        {
            ReviewCreations = reviewCreations,
            AcceptanceCreations = acceptanceCreations.ToHashSet(StringComparer.Ordinal),
            CampaignDecisions = campaignDecisions.ToHashSet(StringComparer.Ordinal),
            RequestFromAcceptance = fromAcceptance
        };
    }

    /// <summary>A field of the interceptor's <c>name=value; …</c> summary of a created row.</summary>
    internal static string? Field(string? summary, string name)
    {
        if (string.IsNullOrEmpty(summary)) return null;
        var match = Regex.Match(summary, $@"(?:^|; ){Regex.Escape(name)}=([^;]*)(?:;|$)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// The reasons <c>RiskAcceptancesService.FlagForReview</c> writes on a revocation or an expiry — the fallback when the
    /// audit row of the request is missing (S39 §6.3 REV-REQ).
    /// </summary>
    internal static bool IsAcceptanceRequestReason(string? reason) =>
        reason is not null &&
        (reason.StartsWith("A risk acceptance was revoked", StringComparison.Ordinal) || AcceptanceExpiredReason().IsMatch(reason));

    [GeneratedRegex(@"^The risk acceptance '.*' expired on \d{4}-\d{2}-\d{2}\.$", RegexOptions.Singleline)]
    private static partial Regex AcceptanceExpiredReason();
}
