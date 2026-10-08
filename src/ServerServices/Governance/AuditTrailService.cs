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
        // Stage 9.5 (S46 §4.10): the flags and the Phase 4 decisions, so a derived flag reverting is
        // visible on the risk's own trail and not only in the table.
        var flagIds = await db.RiskFlags.Where(f => f.RiskId == riskId).Select(f => f.Id).ToListAsync();
        var decisionIds = await db.RiskDecisions.Where(d => d.RiskId == riskId).Select(d => d.Id).ToListAsync();
        // Stage 9.6 (S47 §4.10): the treatment option and cost Gate C computes against, the dependencies Gate D
        // schedules by, and the target level.
        var economicsIds = await db.MitigationEconomics.Where(e => mitigationIds.Contains(e.MitigationId))
            .Select(e => e.Id).ToListAsync();
        var dependencyIds = await db.MitigationDependencies.Where(d => mitigationIds.Contains(d.MitigationId))
            .Select(d => d.Id).ToListAsync();
        var targetIds = await db.RiskTargets.Where(t => t.RiskId == riskId).Select(t => t.Id).ToListAsync();
        // Stage 9.7 (S48 §4.9): the loss components and the correlations the tail statistics are computed from.
        var componentIds = await db.RiskLossComponents.Where(c => c.RiskId == riskId).Select(c => c.Id).ToListAsync();
        var correlationIds = await db.RiskCorrelations.Where(c => c.RiskAId == riskId || c.RiskBId == riskId)
            .Select(c => c.Id).ToListAsync();
        // Stage 9.8 (S49 §4.11): who linked or unlinked an indicator — unlinking removes a gate — and each
        // reassessment trigger raised on the risk.
        var kriLinkIds = await db.KriRisks.Where(l => l.RiskId == riskId).Select(l => l.Id).ToListAsync();
        var triggerIds = await db.RiskReassessmentTriggers.Where(t => t.RiskId == riskId).Select(t => t.Id)
            .ToListAsync();
        // Stage 9.9 (S50 §4.8): the archive — its conditions, reviews and reopening —, the incidents matched to the risk
        // by backtesting, and the committee decisions on it with their votes.
        var archiveIds = await db.RiskArchives.Where(a => a.RiskId == riskId).Select(a => a.Id).ToListAsync();
        var archiveConditionIds = await db.RiskArchiveConditions.Where(c => archiveIds.Contains(c.ArchiveId))
            .Select(c => c.Id).ToListAsync();
        var archiveReviewIds = await db.RiskArchiveReviews.Where(r => archiveIds.Contains(r.ArchiveId))
            .Select(r => r.Id).ToListAsync();
        var backtestLinkIds = await db.IncidentBacktestRisks.Where(l => l.RiskId == riskId).Select(l => l.Id)
            .ToListAsync();
        var committeeDecisionIds = await db.RiskCommitteeDecisions.Where(d => d.RiskId == riskId).Select(d => d.Id)
            .ToListAsync();
        var committeeVoteIds = await db.RiskCommitteeVotes.Where(v => committeeDecisionIds.Contains(v.DecisionId))
            .Select(v => v.Id).ToListAsync();
        // Stage 9.11 (S52 §4.10): who linked a legal requirement, and its note — the register's "requirements" group. Resolved
        // from the current links, so an unlinking stays in audit_logs and is not reached here (S52 R11, S51 R5).
        var requirementLinkIds = await db.RiskLegalRequirements.Where(l => l.RiskId == riskId).Select(l => l.Id)
            .ToListAsync();
        // Stage 9.12 (S53 §4.8): who linked the risk to an inventoried AI model — the link derives flag 11. Resolved from the
        // current links the reader sees (the risk and the model), as the requirement links are.
        var aiModelLinkIds = await db.AiModelRisks.Where(l => l.RiskId == riskId).Select(l => l.Id).ToListAsync();

        return await db.AuditLogs
            .Where(a =>
                (a.EntityType == nameof(Risk) && a.EntityId == riskId) ||
                (a.EntityType == nameof(RiskScoring) && a.EntityId == riskId) ||
                (a.EntityType == nameof(Mitigation) && mitigationIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationTask) && taskIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MgmtReview) && reviewIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskAcceptance) && acceptanceIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskReviewCampaignItem) && campaignItemIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskFlag) && flagIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskDecision) && decisionIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationEconomics) && economicsIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationDependency) && dependencyIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskTarget) && targetIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskLossComponent) && componentIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskCorrelation) && correlationIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(KriRisk) && kriLinkIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskReassessmentTrigger) && triggerIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskArchive) && archiveIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskArchiveCondition) && archiveConditionIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskArchiveReview) && archiveReviewIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(IncidentBacktestRisk) && backtestLinkIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskCommitteeDecision) && committeeDecisionIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskCommitteeVote) && committeeVoteIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskLegalRequirement) && requirementLinkIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(AiModelRisk) && aiModelLinkIds.Contains(a.EntityId)))
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
        // Stage 9.5 (S46 §4.6, §4.10): a withdrawn Gate A declaration is the only way such a risk becomes
        // acceptable again, so its written reason travels with the evidence pack.
        var flagIds = await db.RiskFlags.Where(f => riskIds.Contains(f.RiskId)).Select(f => f.Id).ToListAsync();
        var decisionIds = await db.RiskDecisions.Where(d => riskIds.Contains(d.RiskId)).Select(d => d.Id)
            .ToListAsync();
        // Stage 9.6 (S47 §4.10): the economics, dependencies and targets travel with the evidence pack.
        var economicsIds = await db.MitigationEconomics.Where(e => mitigationIds.Contains(e.MitigationId))
            .Select(e => e.Id).ToListAsync();
        var dependencyIds = await db.MitigationDependencies.Where(d => mitigationIds.Contains(d.MitigationId))
            .Select(d => d.Id).ToListAsync();
        var targetIds = await db.RiskTargets.Where(t => riskIds.Contains(t.RiskId)).Select(t => t.Id).ToListAsync();
        // Stage 9.7 (S48 §4.9): the loss components and correlations travel with the pack, and the appetite's tail
        // tolerances with the appetite.
        var componentIds = await db.RiskLossComponents.Where(c => riskIds.Contains(c.RiskId)).Select(c => c.Id)
            .ToListAsync();
        var correlationIds = await db.RiskCorrelations
            .Where(c => riskIds.Contains(c.RiskAId) || riskIds.Contains(c.RiskBId)).Select(c => c.Id).ToListAsync();
        // Stage 9.8 (S49 §4.11): the indicators that gate the pack's risks — their tolerance, readings and voidings —,
        // the links, and the reassessment events and triggers raised on them.
        var kriLinks = await db.KriRisks.Where(l => riskIds.Contains(l.RiskId)).Select(l => new { l.Id, l.KriId })
            .ToListAsync();
        var kriLinkIds = kriLinks.Select(l => l.Id).ToList();
        var kriIds = kriLinks.Select(l => l.KriId).Distinct().ToList();
        var kriReadingIds = await db.KriReadings.Where(r => kriIds.Contains(r.KriId)).Select(r => r.Id).ToListAsync();
        var triggers = await db.RiskReassessmentTriggers.Where(t => riskIds.Contains(t.RiskId))
            .Select(t => new { t.Id, t.EventId }).ToListAsync();
        var triggerIds = triggers.Select(t => t.Id).ToList();
        var eventIds = triggers.Select(t => t.EventId).Distinct().ToList();
        // Stage 9.9 (S50 §4.8): the archives of the pack's risks with their conditions and reviews, the backtests that
        // matched incidents to them, the committee decisions on them with every vote; the committees themselves and
        // their membership travel like the appetite — organization-wide governance.
        var archiveIds = await db.RiskArchives.Where(a => riskIds.Contains(a.RiskId)).Select(a => a.Id).ToListAsync();
        var archiveConditionIds = await db.RiskArchiveConditions.Where(c => archiveIds.Contains(c.ArchiveId))
            .Select(c => c.Id).ToListAsync();
        var archiveReviewIds = await db.RiskArchiveReviews.Where(r => archiveIds.Contains(r.ArchiveId))
            .Select(r => r.Id).ToListAsync();
        var backtestLinks = await db.IncidentBacktestRisks.Where(l => riskIds.Contains(l.RiskId))
            .Select(l => new { l.Id, l.BacktestId }).ToListAsync();
        var backtestLinkIds = backtestLinks.Select(l => l.Id).ToList();
        var backtestIds = backtestLinks.Select(l => l.BacktestId).Distinct().ToList();
        var committeeDecisionIds = await db.RiskCommitteeDecisions.Where(d => riskIds.Contains(d.RiskId))
            .Select(d => d.Id).ToListAsync();
        var committeeVoteIds = await db.RiskCommitteeVotes.Where(v => committeeDecisionIds.Contains(v.DecisionId))
            .Select(v => v.Id).ToListAsync();
        // Stage 9.11 (S52 §4.10): the legal requirements linked to the pack's risks. The catalogue itself is the
        // organization's and is read through its own history routes.
        var requirementLinkIds = await db.RiskLegalRequirements.Where(l => riskIds.Contains(l.RiskId)).Select(l => l.Id)
            .ToListAsync();
        // Stage 9.12 (S53 §4.8): the links of the pack's risks to inventoried AI models. The inventory itself is read through
        // its own history route, after the model is found visible.
        var aiModelLinkIds = await db.AiModelRisks.Where(l => riskIds.Contains(l.RiskId)).Select(l => l.Id).ToListAsync();

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
                (a.EntityType == nameof(RiskFlag) && flagIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskDecision) && decisionIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationEconomics) && economicsIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(MitigationDependency) && dependencyIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskTarget) && targetIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskLossComponent) && componentIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskCorrelation) && correlationIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(KriRisk) && kriLinkIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(Kri) && kriIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(KriReading) && kriReadingIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskReassessmentTrigger) && triggerIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(ReassessmentEvent) && eventIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskArchive) && archiveIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskArchiveCondition) && archiveConditionIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskArchiveReview) && archiveReviewIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(IncidentBacktest) && backtestIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(IncidentBacktestRisk) && backtestLinkIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskCommitteeDecision) && committeeDecisionIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskCommitteeVote) && committeeVoteIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(RiskLegalRequirement) && requirementLinkIds.Contains(a.EntityId)) ||
                (a.EntityType == nameof(AiModelRisk) && aiModelLinkIds.Contains(a.EntityId)) ||
                a.EntityType == nameof(RiskCommittee) ||
                a.EntityType == nameof(RiskCommitteeMember) ||
                a.EntityType == nameof(RiskAppetite) ||
                a.EntityType == nameof(RiskAppetiteTailLimit))
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
