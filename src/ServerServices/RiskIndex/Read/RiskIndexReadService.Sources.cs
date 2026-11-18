using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    // ------------------------------------------------------------------------------------------------------------------
    // The Sources tab's operational columns and the index runs (S40 §6.5, §8.2; M55, T263)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The most runs one call lists.</summary>
    public const int MaxRuns = 100;

    /// <summary>How long the organization's per-source counts are reused; a re-run snapshot of the same day refreshes within it.</summary>
    private static readonly TimeSpan SourceCountsTimeToLive = TimeSpan.FromMinutes(15);

    /// <summary>The importers that are a source of their own, not of "other importers" (S39 §3.2; the collector's own split).</summary>
    private static readonly string[] OwnImporters = ["trendmicro-visionone", "securityscorecard", "assessment", "nessus", "tenable-vm"];

    private const string NessusImporter = "nessus";

    public async Task<IReadOnlyList<RiskIndexSourceDto>> GetSourcesAsync(RiskIndexCaller caller, CancellationToken ct = default)
    {
        // Not a value of the index, so no shadow rule (S40 §8.2); the counts are the organization's whole inventory, so they
        // go to global scope only (S39 §10.4) and are withheld — not computed and then hidden — for everyone else.
        var unrestricted = dalService.GetCurrentEntityScope().IsUnrestricted;
        await using var db = dalService.GetContext();
        var status = await SourceStatusAsync(db, ct);

        SourceCounts? counts = null;
        if (unrestricted && await LatestDateAsync(db, ct) is { } date && await ProfileOfAsync(db, date, ct) is { } profile)
            counts = await cache.GetOrCreateAsync(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"sources|{date:yyyy-MM-dd}|{profile.Id}"),
                SourceCountsTimeToLive, () => SourceCountsAsync(db, date, profile.Id, ct));

        return RiskIndexSources.All.Select(id =>
        {
            var line = status.GetValueOrDefault(id);
            var tracked = RiskIndexSourceBits.Bit(id) != 0;
            int? objects = counts is not null && tracked ? counts.Objects.GetValueOrDefault(id) : null;
            double? stale = counts is not null && tracked
                ? objects > 0 ? (double)counts.Stale.GetValueOrDefault(id) / objects.Value : 0
                : null;
            return new RiskIndexSourceDto(id, line?.Connections, line?.LastSyncUtc, line?.StatusKey, line?.KindKey, objects,
                stale, !unrestricted && tracked);
        }).ToList();
    }

    public async Task<IReadOnlyList<IntegrationSyncLog>> GetRunsAsync(RiskIndexRunKind kind, int limit, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        if (!dalService.GetCurrentEntityScope().IsUnrestricted)
            throw RiskIndexException.Forbidden(RiskIndexErrors.RunsRequireGlobalScope,
                "The index runs carry the organization's row counts: they need global scope.");
        if (limit is < 1 or > MaxRuns)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidLimit, $"The limit is between 1 and {MaxRuns}.");

        await using var db = dalService.GetContext();
        switch (kind)
        {
            case RiskIndexRunKind.Snapshot:
                var runs = await db.IntegrationSyncLogs.AsNoTracking()
                    .Where(l => l.Integration == IntegrationKind.RiskIndex)
                    .OrderByDescending(l => l.Id).Take(limit)
                    .ToListAsync(ct);
                // The progress trail is the run's diagnosis, not its summary: the tab shows when, how it ended and the counts.
                foreach (var run in runs) run.ProgressLog = null;
                return runs;

            case RiskIndexRunKind.Preview:
                var previews = await db.RiskIndexPreviewRequests.AsNoTracking()
                    .OrderByDescending(p => p.Id).Take(limit)
                    .Select(p => new { p.Id, p.ProfileId, p.Profile.Version, p.Status, p.CreatedAt, p.StartedAt, p.FinishedAt,
                        p.ErrorMessage })
                    .ToListAsync(ct);
                return previews.Select(p => new IntegrationSyncLog
                {
                    Id = p.Id, Integration = IntegrationKind.RiskIndex, ConnectionId = p.ProfileId,
                    ConnectionName = $"Preview of profile v{p.Version}", StartedAt = p.StartedAt ?? p.CreatedAt,
                    FinishedAt = p.FinishedAt, Status = PreviewStatus(p.Status), ErrorMessage = p.ErrorMessage
                }).ToList();

            case RiskIndexRunKind.Retention:
                throw RiskIndexException.BadRequest(RiskIndexErrors.RunKindNotRecorded,
                    "The retention pass does not record its runs yet.");

            default:
                throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidRunKind,
                    "The run kind is Snapshot (1), Retention (2) or Preview (3).");
        }
    }

    internal static IntegrationSyncStatus PreviewStatus(RiskIndexPreviewStatus status) => status switch
    {
        RiskIndexPreviewStatus.Completed => IntegrationSyncStatus.Succeeded,
        RiskIndexPreviewStatus.Failed => IntegrationSyncStatus.Failed,
        _ => IntegrationSyncStatus.Running
    };

    /// <summary>One source's connections and last sync or import.</summary>
    internal sealed record SourceStatus(int? Connections, DateTime? LastSyncUtc, string? StatusKey, string? KindKey);

    /// <summary>
    /// What the configuration knows about each source, for every reader: Vision One feeds V1-DEV (connections syncing risk
    /// scores) and V1-CVE (syncing vulnerabilities), SecurityScorecard feeds SSC-TOT, Nessus files TEN-NES and the other
    /// importers IMPORTERS. Imports pass the scan-import filter, so a scoped reader sees the last import of their scope.
    /// </summary>
    private static async Task<Dictionary<string, SourceStatus>> SourceStatusAsync(NRDbContext db, CancellationToken ct)
    {
        var lines = new Dictionary<string, SourceStatus>(StringComparer.Ordinal);

        var v1 = await db.TrendMicroConnections.AsNoTracking().Where(c => c.Enabled)
            .Select(c => new SyncedConnection(c.SyncRiskScores, c.SyncVulnerabilities, c.LastSyncAt, c.LastSyncStatus))
            .ToListAsync(ct);
        lines[RiskIndexSources.V1Device] = FromConnections(v1.Where(c => c.Scores).ToList());
        lines[RiskIndexSources.V1Cve] = FromConnections(v1.Where(c => c.Vulnerabilities).ToList());

        var ssc = await db.SecurityScorecardConnections.AsNoTracking().Where(c => c.Enabled)
            .Select(c => new SyncedConnection(true, true, c.LastSyncAt, c.LastSyncStatus))
            .ToListAsync(ct);
        lines[RiskIndexSources.SscTotal] = FromConnections(ssc);

        // M56, T266–T267: Tenable VM feeds TEN-VM (connections syncing findings), TEN-AES and TEN-ACR (syncing assets and
        // collecting the rating).
        var tenable = await db.TenableConnections.AsNoTracking().Where(c => c.Enabled)
            .Select(c => new { c.SyncFindings, c.SyncAssets, c.CollectAes, c.CollectAcr, c.LastSyncAt, c.LastSyncStatus })
            .ToListAsync(ct);
        lines[RiskIndexSources.TenableVm] = FromConnections(tenable.Where(c => c.SyncFindings)
            .Select(c => new SyncedConnection(false, true, c.LastSyncAt, c.LastSyncStatus)).ToList());
        lines[RiskIndexSources.TenableAes] = FromConnections(tenable.Where(c => c.SyncAssets && c.CollectAes)
            .Select(c => new SyncedConnection(true, false, c.LastSyncAt, c.LastSyncStatus)).ToList());
        lines[RiskIndexSources.TenableAcr] = FromConnections(tenable.Where(c => c.SyncAssets && c.CollectAcr)
            .Select(c => new SyncedConnection(true, false, c.LastSyncAt, c.LastSyncStatus)).ToList());

        var nessus = await db.ScanImports.AsNoTracking()
            .Where(i => i.Importer.ToLower() == NessusImporter)
            .OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.Id)
            .Select(i => new { i.StartedAt, i.FinishedAt, i.Status })
            .FirstOrDefaultAsync(ct);
        lines[RiskIndexSources.TenableNessus] = new SourceStatus(null, nessus?.FinishedAt ?? nessus?.StartedAt,
            nessus is null ? null : ImportStatusKey(nessus.Status), RiskIndexSourceStatusKeys.KindImport);

        var other = await db.ScanImports.AsNoTracking()
            .Where(i => !OwnImporters.Contains(i.Importer.ToLower()))
            .OrderByDescending(i => i.StartedAt).ThenByDescending(i => i.Id)
            .Select(i => new { i.StartedAt, i.FinishedAt, i.Status })
            .FirstOrDefaultAsync(ct);
        lines[RiskIndexSources.Importers] = new SourceStatus(null, other?.FinishedAt ?? other?.StartedAt,
            other is null ? null : ImportStatusKey(other.Status), RiskIndexSourceStatusKeys.KindImport);

        return lines;
    }

    private sealed record SyncedConnection(bool Scores, bool Vulnerabilities, DateTime? LastSyncAt,
        IntegrationSyncStatus? LastSyncStatus);

    /// <summary>The count of enabled connections and the most recent sync among them, with how it ended.</summary>
    private static SourceStatus FromConnections(IReadOnlyList<SyncedConnection> connections)
    {
        var last = connections.Where(c => c.LastSyncAt is not null).MaxBy(c => c.LastSyncAt);
        return new SourceStatus(connections.Count, last?.LastSyncAt, last?.LastSyncStatus is { } s ? SyncStatusKey(s) : null,
            RiskIndexSourceStatusKeys.KindSync);
    }

    internal static string SyncStatusKey(IntegrationSyncStatus status) => status switch
    {
        IntegrationSyncStatus.Succeeded => RiskIndexSourceStatusKeys.Ok,
        IntegrationSyncStatus.PartiallySucceeded => RiskIndexSourceStatusKeys.Partial,
        IntegrationSyncStatus.Failed => RiskIndexSourceStatusKeys.Failed,
        IntegrationSyncStatus.Queued => RiskIndexSourceStatusKeys.Queued,
        _ => RiskIndexSourceStatusKeys.Running
    };

    internal static string ImportStatusKey(int status) => (ScanImportStatus)status switch
    {
        ScanImportStatus.Succeeded => RiskIndexSourceStatusKeys.Ok,
        ScanImportStatus.Failed => RiskIndexSourceStatusKeys.Failed,
        ScanImportStatus.Queued => RiskIndexSourceStatusKeys.Queued,
        ScanImportStatus.Cancelled => RiskIndexSourceStatusKeys.Cancelled,
        _ => RiskIndexSourceStatusKeys.Running
    };

    /// <summary>The organization's objects per source on one snapshot day, and how many of them are stale for that source.</summary>
    internal sealed record SourceCounts(IReadOnlyDictionary<string, int> Objects, IReadOnlyDictionary<string, int> Stale);

    private static async Task<SourceCounts> SourceCountsAsync(NRDbContext db, DateOnly date, int profileId, CancellationToken ct)
    {
        var rows = await db.RiskIndexObjectDays.AsNoTracking()
            .Where(o => o.SnapshotDate == date && o.ProfileId == profileId && o.SourceMask != 0)
            .Select(o => new SourceRow(o.ObjectKind, o.ObjectRef, o.SourceMask, o.Factors))
            .ToListAsync(ct);
        return CountSources(rows);
    }

    /// <summary>One object row as the counts read it.</summary>
    internal sealed record SourceRow(RiskIndexObjectKind Kind, string Ref, int SourceMask, string? Factors);

    /// <summary>
    /// The pure half of the counts: an object counts once per source whose bit it carries on any of its category rows; it is
    /// stale for the source when it has readings from it and none is current (every <c>v &lt; 1</c>, the rule of S39 §8.6
    /// "fontes obsoletas" applied per object).
    /// </summary>
    internal static SourceCounts CountSources(IEnumerable<SourceRow> rows)
    {
        var state = new Dictionary<(string Source, IcrObjectKey Key), (bool Read, bool Fresh)>();
        foreach (var row in rows)
        {
            var factors = RiskIndexStoredJson.Deserialize<List<StoredFactor>>(row.Factors) ?? [];
            var key = new IcrObjectKey(row.Kind, row.Ref);
            foreach (var source in RiskIndexSourceBits.Sources.Where(s => RiskIndexSourceBits.Has(row.SourceMask, s)))
            {
                var readings = factors.Where(f => f.Source == source).ToList();
                var known = state.GetValueOrDefault((source, key));
                state[(source, key)] = (known.Read || readings.Count > 0, known.Fresh || readings.Any(f => f.V >= 1));
            }
        }

        var objects = state.GroupBy(s => s.Key.Source).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var stale = state.Where(s => s.Value is { Read: true, Fresh: false }).GroupBy(s => s.Key.Source)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return new SourceCounts(objects, stale);
    }
}
