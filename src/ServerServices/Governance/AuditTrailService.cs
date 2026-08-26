using DAL.Auditing;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ServerServices.Interfaces;
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

    public async Task<int> ApplyRetentionAsync(DateTime asOfUtc)
    {
        await using var db = DalService.GetContext();

        var days = DefaultRetentionDays;
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == RetentionSetting);
        if (setting?.Value is not null && int.TryParse(setting.Value, out var configured) && configured > 0)
            days = configured;

        var cutoff = asOfUtc.AddDays(-days);

        var deleted = await db.AuditLogs.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync();

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
