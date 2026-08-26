using DAL.Auditing;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ServerServices.Interfaces;
using Model.Governance;
using ServerServices.Services;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.4 — the read and retention side of the field-level trail.
/// </summary>
public class AuditTrailService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IAuditTrailService
{
    public const string RetentionSetting = "audit_log_retention_days";

    /// <summary>Five years — a SOC 2 Type II look-back plus margin. Overridable in settings.</summary>
    public const int DefaultRetentionDays = 1825;

    public async Task<List<AuditLog>> GetForRecordAsync(string entityType, int entityId, int limit = 500)
    {
        await using var db = DalService.GetContext();

        return await db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetForRiskAsync(int riskId, int limit = 1000)
    {
        await using var db = DalService.GetContext();

        // The aggregate's children, resolved to ids first. A join over entity_type/entity_id pairs
        // is not expressible in one query because the trail is polymorphic by design — the price of
        // one table covering every audited type.
        var mitigationIds = await db.Mitigations.Where(m => m.RiskId == riskId).Select(m => m.Id)
            .ToListAsync();
        var taskIds = await db.MitigationTasks.Where(t => mitigationIds.Contains(t.MitigationId))
            .Select(t => t.Id).ToListAsync();
        var reviewIds = await db.MgmtReviews.Where(r => r.RiskId == riskId).Select(r => r.Id).ToListAsync();
        var acceptanceIds = await db.RiskAcceptances.Where(a => a.RiskId == riskId).Select(a => a.Id)
            .ToListAsync();
        var campaignItemIds = await db.RiskReviewCampaignItems.Where(i => i.RiskId == riskId)
            .Select(i => i.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a =>
                (a.EntityType == nameof(Risk) && a.EntityId == riskId) ||
                (a.EntityType == nameof(RiskScoring) && a.EntityId == riskId) ||
                (a.EntityType == nameof(Mitigation) && mitigationIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationTask) && taskIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MgmtReview) && reviewIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskAcceptance) && acceptanceIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskReviewCampaignItem) && campaignItemIds.Contains(a.EntityId)))
            .Include(a => a.User)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetForEntityPeriodAsync(int? entityId, DateTime fromUtc,
        DateTime toUtc, int limit = 20000)
    {
        await using var db = DalService.GetContext();

        var riskIds = await db.Risks
            .Where(r => entityId == null || r.EntityId == entityId)
            .Select(r => r.Id)
            .ToListAsync();

        var mitigationIds = await db.Mitigations.Where(m => riskIds.Contains(m.RiskId)).Select(m => m.Id)
            .ToListAsync();
        var taskIds = await db.MitigationTasks.Where(t => mitigationIds.Contains(t.MitigationId))
            .Select(t => t.Id).ToListAsync();
        var reviewIds = await db.MgmtReviews.Where(r => riskIds.Contains(r.RiskId)).Select(r => r.Id)
            .ToListAsync();
        var acceptanceIds = await db.RiskAcceptances
            .Where(a => a.RiskId != null && riskIds.Contains(a.RiskId.Value)).Select(a => a.Id)
            .ToListAsync();
        var campaignItemIds = await db.RiskReviewCampaignItems.Where(i => riskIds.Contains(i.RiskId))
            .Select(i => i.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a => a.OccurredAt >= fromUtc && a.OccurredAt <= toUtc)
            .Where(a =>
                ((a.EntityType == nameof(Risk) || a.EntityType == nameof(RiskScoring)) &&
                 riskIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(Mitigation) && mitigationIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationTask) && taskIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MgmtReview) && reviewIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskAcceptance) && acceptanceIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskReviewCampaignItem) && campaignItemIds.Contains(a.EntityId)) ||
                a.EntityType == nameof(RiskAppetite))
            .Include(a => a.User)
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => a.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<GovernanceEvidencePack> GetEvidencePackAsync(int? entityId, DateTime fromUtc,
        DateTime toUtc, string requestedBy, int changeLimit = 20000)
    {
        await using var db = DalService.GetContext();

        var pack = new GovernanceEvidencePack
        {
            EntityId = entityId,
            EntityName = await ResolveEntityNameAsync(db, entityId),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            GeneratedAtUtc = DateTime.UtcNow,
            RequestedBy = requestedBy
        };

        var risks = await db.Risks
            .Where(r => entityId == null || r.EntityId == entityId)
            .Select(r => new { r.Id, r.Subject })
            .ToListAsync();

        var subjects = risks.ToDictionary(r => r.Id, r => r.Subject ?? "");
        var riskIds = subjects.Keys.ToList();

        // Acceptances that overlap the period, not only those created in it. An exception granted
        // last year and still in force is the single most relevant fact about an entity's posture,
        // and an export that omitted it would be evidence of the wrong thing.
        var acceptances = await db.RiskAcceptances
            .Where(a => a.RiskId != null && riskIds.Contains(a.RiskId.Value))
            .Where(a => a.StartDate <= toUtc && (a.RevokedAt == null || a.RevokedAt >= fromUtc))
            .Where(a => a.ExpiresAt >= fromUtc || a.CreatedAt <= toUtc)
            .Include(a => a.AuthorizingManager)
            .Include(a => a.RequestedBy)
            .Include(a => a.RevokedBy)
            .OrderBy(a => a.StartDate)
            .ToListAsync();

        var campaignAcceptanceIds = (await db.RiskReviewCampaignItems
                .Where(i => i.RiskAcceptanceId != null)
                .Select(i => i.RiskAcceptanceId!.Value)
                .ToListAsync())
            .ToHashSet();

        pack.Acceptances = acceptances.Select(a => new EvidenceAcceptance
        {
            Id = a.Id,
            RiskId = a.RiskId,
            RiskSubject = a.RiskId != null && subjects.TryGetValue(a.RiskId.Value, out var rs) ? rs : "",
            Name = a.Name,
            Status = a.Status.ToString(),
            AuthorizingManager = Describe(a.AuthorizingManager),
            RequestedBy = a.RequestedBy == null ? null : Describe(a.RequestedBy),
            StartDate = a.StartDate,
            ExpiresAt = a.ExpiresAt,
            RevokedAt = a.RevokedAt,
            RevokedBy = a.RevokedBy == null ? null : Describe(a.RevokedBy),
            RevocationReason = a.RevocationReason,
            BusinessJustification = a.BusinessJustification,
            CompensatingControls = a.CompensatingControls,
            ResidualScoreSnapshot = a.ResidualScoreSnapshot,
            FromCampaign = campaignAcceptanceIds.Contains(a.Id)
        }).ToList();

        var reviews = await db.MgmtReviews
            .Where(r => riskIds.Contains(r.RiskId))
            .Where(r => r.SubmissionDate >= fromUtc && r.SubmissionDate <= toUtc)
            .Include(r => r.ReviewerNavigation)
            .Include(r => r.SecondReviewer)
            .OrderBy(r => r.SubmissionDate)
            .ToListAsync();

        pack.Reviews = reviews.Select(r => new EvidenceReview
        {
            Id = r.Id,
            RiskId = r.RiskId,
            RiskSubject = subjects.TryGetValue(r.RiskId, out var subject) ? subject : "",
            SubmissionDate = r.SubmissionDate,
            Reviewer = Describe(r.ReviewerNavigation),
            Comments = r.Comments,
            RequiresCountersignature = r.RequiresCountersignature,
            SecondReviewer = r.SecondReviewer == null ? null : Describe(r.SecondReviewer),
            SecondReviewAt = r.SecondReviewAt,
            SegregationOverrideReason = r.SegregationOverrideReason
        }).ToList();

        // Campaign evidence (8.6.5). Selected by campaign period overlap rather than by decision
        // date: a campaign nobody decided is itself the finding, and a decision list that silently
        // dropped the undecided items would read as a complete review.
        var items = await db.RiskReviewCampaignItems
            .Where(i => riskIds.Contains(i.RiskId))
            .Include(i => i.Campaign)
            .Include(i => i.DecidedBy)
            .Include(i => i.EscalatedTo)
            .Where(i => i.Campaign != null && i.Campaign.PeriodStart <= toUtc &&
                        i.Campaign.PeriodEnd >= fromUtc)
            .ToListAsync();

        pack.CampaignDecisions = items
            .OrderBy(i => i.Campaign!.PeriodStart)
            .ThenBy(i => i.Rank ?? int.MaxValue)
            .ThenBy(i => i.Id)
            .Select(i => new EvidenceCampaignDecision
            {
                CampaignId = i.CampaignId,
                CampaignName = i.Campaign!.Name,
                PeriodStart = i.Campaign.PeriodStart,
                PeriodEnd = i.Campaign.PeriodEnd,
                DueDate = i.Campaign.DueDate,
                CampaignStatus = i.Campaign.Status.ToString(),
                RiskId = i.RiskId,
                RiskSubject = subjects.TryGetValue(i.RiskId, out var subject) ? subject : "",
                Rank = i.Rank,
                Decision = i.Decision.ToString(),
                DecisionNotes = i.DecisionNotes,
                DecidedBy = i.DecidedBy == null ? null : Describe(i.DecidedBy),
                DecidedAt = i.DecidedAt,
                EscalatedTo = i.EscalatedTo == null ? null : Describe(i.EscalatedTo),
                RiskAcceptanceId = i.RiskAcceptanceId
            })
            .ToList();

        // One more than asked for, so "truncated" is a fact rather than a guess about whether the
        // last page happened to be exactly full.
        var changes = await GetForEntityPeriodAsync(entityId, fromUtc, toUtc, changeLimit + 1);

        pack.ChangesTruncated = changes.Count > changeLimit;

        pack.Changes = changes.Take(changeLimit).Select(a => new EvidenceChange
        {
            OccurredAt = a.OccurredAt,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            Field = a.Field,
            Action = a.Action.ToString(),
            Actor = a.Actor,
            UserId = a.UserId,
            OldValue = a.OldValue,
            NewValue = a.NewValue,
            CorrelationId = a.CorrelationId
        }).ToList();

        return pack;
    }

    /// <summary>
    /// A person's name and login, because an evidence file whose actor column holds "412" is not
    /// evidence anybody can read. The id stays alongside for the cases where two people share a name.
    /// </summary>
    private static string Describe(User? user) =>
        user == null ? "" : $"{user.Name} ({user.Login}, #{user.Value})";

    /// <summary>
    /// The entity's display name, which lives in an <c>entities_properties</c> row rather than on the
    /// entity — so a missing name row degrades to the id rather than throwing during an export.
    /// </summary>
    private static async Task<string> ResolveEntityNameAsync(DAL.Context.AuditableContext db, int? entityId)
    {
        if (entityId is null) return "(all entities)";

        var name = await db.EntitiesProperties
            .Where(p => p.Entity == entityId && p.Type == "name")
            .Select(p => p.Value)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(name) ? $"#{entityId}" : name;
    }

    public async Task<int> ApplyRetentionAsync(DateTime asOfUtc)
    {
        await using var db = DalService.GetContext();

        var days = DefaultRetentionDays;
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == RetentionSetting);
        if (setting?.Value is not null && int.TryParse(setting.Value, out var configured) && configured > 0)
            days = configured;

        var cutoff = asOfUtc.AddDays(-days);

        // Batched RemoveRange rather than ExecuteDelete. ExecuteDelete would be one statement, but it
        // is unsupported by the EF in-memory provider the service tests run on, and a retention pass
        // that cannot be tested is a retention pass nobody can trust. Batching keeps the memory cost
        // bounded on the first run after a long retention window, which is when this deletes most.
        const int batchSize = 5_000;
        var deleted = 0;

        while (true)
        {
            var batch = await db.AuditLogs
                .Where(a => a.OccurredAt < cutoff)
                .OrderBy(a => a.Id)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0) break;

            db.AuditLogs.RemoveRange(batch);
            await db.SaveChangesAsync();

            deleted += batch.Count;

            if (batch.Count < batchSize) break;
        }

        if (deleted > 0)
            Logger.Information("Audit-trail retention removed {Count} rows older than {Cutoff:yyyy-MM-dd} " +
                               "({Days} day policy)", deleted, cutoff, days);

        return deleted;
    }

    /// <summary>
    /// The set of CLR type names the interceptor writes rows for. Exposed so the API and the tests
    /// can state the scope without duplicating the list.
    /// </summary>
    public static IReadOnlyCollection<string> AuditedTypes => GovernanceAuditInterceptor.AuditedTypes;
}
