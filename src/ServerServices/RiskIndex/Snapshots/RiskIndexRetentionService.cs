using DAL.Context;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.RiskIndex;

/// <summary>What one retention pass removed or cleared, per table.</summary>
public sealed record RiskIndexRetentionCounts(int ObjectDays, int Attributions, int ClassDays, int Snapshots, int NodeMembers,
    int DetailCleared, int IncidentClosures, int PreviewReportsCleared = 0)
{
    public int Deleted => ObjectDays + Attributions + ClassDays + Snapshots + NodeMembers + IncidentClosures;
}

/// <summary>The only purge path of the index's history (S39 §9.1; S40 §9.6) — what the 02:40 job runs.</summary>
public interface IRiskIndexRetentionService
{
    Task<RiskIndexRetentionCounts> ApplyAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// M53, T247 (S40 §7.7, §9.6): object rows, attributions, class days and frozen closures after 400 days; snapshots after
/// 1825 days, and their node members with them — never before, because a scoped official read compares the claims with
/// them; the JSON detail of snapshots older than 400 days cleared, since it no longer replays. Batches of 10 000.
/// </summary>
public sealed class RiskIndexRetentionService(IDalService dalService, ILogger logger) : IRiskIndexRetentionService
{
    public const int ObjectRetentionDays = 400;
    public const int SnapshotRetentionDays = 1825;
    public const int DefaultBatchSize = 10_000;

    /// <summary>A finished preview keeps its report this long; the one a submission attached lives on the profile (S40 §9.6).</summary>
    public const int PreviewReportRetentionDays = 90;

    /// <summary>The batch size; tests lower it to exercise several batches.</summary>
    internal int BatchSize { get; set; } = DefaultBatchSize;

    public async Task<RiskIndexRetentionCounts> ApplyAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var objectCutoff = today.AddDays(-ObjectRetentionDays);
        var snapshotCutoff = today.AddDays(-SnapshotRetentionDays);

        // Deleted by key, loading ids only: the JSON columns of an object row are what makes the table large.
        var objectDays = await DeleteAsync(db => db.RiskIndexObjectDays.Where(o => o.SnapshotDate < objectCutoff)
            .OrderBy(o => o.Id).Select(o => o.Id), id => new RiskIndexObjectDay { Id = id }, cancellationToken);
        var attributions = await DeleteAsync(db => db.RiskIndexObjectAttributions.Where(a => a.SnapshotDate < objectCutoff)
            .OrderBy(a => a.Id).Select(a => a.Id), id => new RiskIndexObjectAttribution { Id = id }, cancellationToken);
        var classDays = await DeleteAsync(db => db.RiskIndexClassDays.Where(c => c.SnapshotDate < objectCutoff)
            .OrderBy(c => c.Id).Select(c => c.Id), id => new RiskIndexClassDay { Id = id }, cancellationToken);
        var snapshots = await DeleteAsync(db => db.RiskIndexSnapshots.Where(s => s.SnapshotDate < snapshotCutoff)
            .OrderBy(s => s.Id).Select(s => s.Id), id => new RiskIndexSnapshot { Id = id }, cancellationToken);
        var members = await DeleteAsync(db => db.RiskIndexNodeMembers.Where(m => m.SnapshotDate < snapshotCutoff)
            .OrderBy(m => m.Id).Select(m => m.Id), id => new RiskIndexNodeMember { Id = id }, cancellationToken);

        var closureCutoff = nowUtc.AddDays(-ObjectRetentionDays);
        var closures = await DeleteAsync(db => db.RiskIndexIncidentClosures.Where(c => c.ClosedAt < closureCutoff)
            .OrderBy(c => c.IncidentId).Select(c => c.IncidentId), id => new RiskIndexIncidentClosure { IncidentId = id },
            cancellationToken);

        var cleared = await ClearDetailAsync(objectCutoff, cancellationToken);
        var previews = await ClearPreviewReportsAsync(nowUtc.AddDays(-PreviewReportRetentionDays), cancellationToken);

        var counts = new RiskIndexRetentionCounts(objectDays, attributions, classDays, snapshots, members, cleared, closures,
            previews);
        if (counts.Deleted == 0 && cleared == 0 && previews == 0)
            logger.Debug("Risk index retention found nothing to remove");
        else
            logger.Information(
                "Risk index retention removed {ObjectDays} object row(s), {Attributions} attribution(s), {ClassDays} class " +
                "day(s), {Snapshots} snapshot row(s), {Members} node member(s), {Closures} frozen closure(s), and cleared the " +
                "detail of {Cleared} snapshot row(s) and {Previews} preview report(s)", objectDays, attributions, classDays, snapshots,
                members, closures, cleared, previews);
        return counts;
    }

    /// <summary>Deletes what <paramref name="ids"/> selects, by key, a batch at a time; returns how many rows went.</summary>
    private async Task<int> DeleteAsync<T, TKey>(Func<AuditableContext, IQueryable<TKey>> ids, Func<TKey, T> stub,
        CancellationToken ct) where T : class
    {
        var deleted = 0;
        await using var db = dalService.GetContext();
        while (true)
        {
            var batch = await ids(db).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            db.Set<T>().RemoveRange(batch.Select(stub));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            deleted += batch.Count;
            if (batch.Count < BatchSize) break;
        }

        return deleted;
    }

    /// <summary>Nulls <c>top_contributions</c> and <c>change_attribution</c> of snapshots older than 400 days (S40 §7.7).</summary>
    private async Task<int> ClearDetailAsync(DateOnly cutoff, CancellationToken ct)
    {
        var cleared = 0;
        await using var db = dalService.GetContext();
        while (true)
        {
            var batch = await db.RiskIndexSnapshots
                .Where(s => s.SnapshotDate < cutoff && (s.TopContributions != null || s.ChangeAttribution != null))
                .OrderBy(s => s.Id).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                row.TopContributions = null;
                row.ChangeAttribution = null;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            cleared += batch.Count;
            if (batch.Count < BatchSize) break;
        }

        return cleared;
    }

    /// <summary>Nulls the report of previews finished more than 90 days ago (S40 §9.6).</summary>
    private async Task<int> ClearPreviewReportsAsync(DateTime cutoff, CancellationToken ct)
    {
        var cleared = 0;
        await using var db = dalService.GetContext();
        while (true)
        {
            var batch = await db.RiskIndexPreviewRequests
                .Where(r => r.FinishedAt != null && r.FinishedAt < cutoff && r.Report != null)
                .OrderBy(r => r.Id).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var row in batch) row.Report = null;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            cleared += batch.Count;
            if (batch.Count < BatchSize) break;
        }

        return cleared;
    }
}
