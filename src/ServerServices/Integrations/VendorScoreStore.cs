using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Integrations;

/// <summary>One host reading a sync wants to keep (S40 §7.2 <c>vendor_scores</c>).</summary>
public sealed record VendorScoreReading(VendorScoreSource Source, int ConnectionId, int HostId, string? ExternalRef,
    int? EntityId, double Value, double ScaleMax, string? Details);

/// <summary>
/// The piecewise-constant writer of <c>vendor_scores</c> (S40 §7.2; M56, T267): a reading equal to the host's current one
/// for that source only extends it (<c>last_captured_at</c>, entity, details); a different value opens a new row. History
/// and freshness without a row per host per day.
/// </summary>
public static class VendorScoreStore
{
    /// <summary>Values closer than this are the same reading.</summary>
    private const double Tolerance = 1e-9;

    private const int LookupBatchSize = 500;

    /// <summary>Applies <paramref name="readings"/> to <paramref name="db"/> without saving; returns how many rows it opened.</summary>
    public static async Task<int> ApplyAsync(NRDbContext db, IReadOnlyList<VendorScoreReading> readings, DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (readings.Count == 0) return 0;

        var current = new Dictionary<(VendorScoreSource, int), VendorScore>();
        var sources = readings.Select(r => r.Source).Distinct().ToList();
        var hostIds = readings.Select(r => r.HostId).Distinct().ToList();

        for (var offset = 0; offset < hostIds.Count; offset += LookupBatchSize)
        {
            var slice = hostIds.GetRange(offset, Math.Min(LookupBatchSize, hostIds.Count - offset));
            var rows = await db.VendorScores
                .Where(v => v.HostId != null && slice.Contains(v.HostId.Value) && sources.Contains(v.Source))
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                var key = (row.Source, row.HostId!.Value);
                if (!current.TryGetValue(key, out var known) || Later(row, known)) current[key] = row;
            }
        }

        var opened = 0;
        foreach (var reading in readings)
        {
            var key = (reading.Source, reading.HostId);
            if (current.TryGetValue(key, out var row) && Math.Abs(row.Value - reading.Value) < Tolerance)
            {
                row.LastCapturedAt = nowUtc;
                row.EntityId = reading.EntityId;
                row.ConnectionId = reading.ConnectionId;
                row.ExternalRef = reading.ExternalRef;
                row.Details = reading.Details;
                row.UpdatedAt = nowUtc;
                continue;
            }

            var opening = new VendorScore
            {
                Source = reading.Source, ConnectionId = reading.ConnectionId, ObjectKind = VendorScoreObjectKind.Host,
                HostId = reading.HostId, ExternalRef = reading.ExternalRef, EntityId = reading.EntityId,
                Value = reading.Value, ScaleMax = reading.ScaleMax, Details = reading.Details,
                FirstCapturedAt = nowUtc, LastCapturedAt = nowUtc, CreatedAt = nowUtc
            };
            db.VendorScores.Add(opening);
            current[key] = opening;
            opened++;
        }

        return opened;
    }

    /// <summary>
    /// The same piecewise-constant write for readings that are not about a host (M57, T271–T273): a vendor asset keyed by
    /// its row, a tenant or an asset group keyed by <see cref="VendorObjectReading.ExternalRef"/> within its connection.
    /// Applies without saving; returns how many rows it opened.
    /// </summary>
    public static async Task<int> ApplyObjectsAsync(NRDbContext db, IReadOnlyList<VendorObjectReading> readings,
        DateTime nowUtc, CancellationToken ct = default)
    {
        if (readings.Count == 0) return 0;

        var current = new Dictionary<ObjectKey, VendorScore>();
        foreach (var group in readings.GroupBy(r => (r.Source, r.ConnectionId, r.ObjectKind)))
        {
            var (source, connection, kind) = group.Key;
            var query = db.VendorScores.Where(v => v.Source == source && v.ConnectionId == connection && v.ObjectKind == kind);

            var assetIds = group.Where(r => r.VendorAssetId != null).Select(r => r.VendorAssetId!.Value).Distinct().ToList();
            var rows = new List<VendorScore>();

            for (var offset = 0; offset < assetIds.Count; offset += LookupBatchSize)
            {
                var slice = assetIds.GetRange(offset, Math.Min(LookupBatchSize, assetIds.Count - offset));
                rows.AddRange(await query.Where(v => v.VendorAssetId != null && slice.Contains(v.VendorAssetId.Value))
                    .ToListAsync(ct));
            }

            // A tenant or an asset group: few objects per connection, so its rows are read whole and matched without case
            // in memory, rather than trusting the database's collation (or a provider without one) to fold the reference.
            if (group.Any(r => r.VendorAssetId == null))
                rows.AddRange(await query.Where(v => v.VendorAssetId == null).ToListAsync(ct));

            foreach (var row in rows)
            {
                var key = ObjectKey.Of(row.Source, row.ConnectionId, row.ObjectKind, row.VendorAssetId, row.ExternalRef);
                if (!current.TryGetValue(key, out var known) || Later(row, known)) current[key] = row;
            }
        }

        var opened = 0;
        foreach (var reading in readings)
        {
            var key = ObjectKey.Of(reading.Source, reading.ConnectionId, reading.ObjectKind, reading.VendorAssetId,
                reading.ExternalRef);
            if (current.TryGetValue(key, out var row) && Math.Abs(row.Value - reading.Value) < Tolerance)
            {
                row.LastCapturedAt = nowUtc;
                row.EntityId = reading.EntityId;
                row.Level = reading.Level;
                row.Details = reading.Details;
                row.UpdatedAt = nowUtc;
                continue;
            }

            var opening = new VendorScore
            {
                Source = reading.Source, ConnectionId = reading.ConnectionId, ObjectKind = reading.ObjectKind,
                VendorAssetId = reading.VendorAssetId, ExternalRef = reading.ExternalRef, EntityId = reading.EntityId,
                Value = reading.Value, ScaleMax = reading.ScaleMax, Level = reading.Level, Details = reading.Details,
                FirstCapturedAt = nowUtc, LastCapturedAt = nowUtc, CreatedAt = nowUtc
            };
            db.VendorScores.Add(opening);
            current[key] = opening;
            opened++;
        }

        return opened;
    }

    private static bool Later(VendorScore a, VendorScore b) =>
        a.LastCapturedAt > b.LastCapturedAt || (a.LastCapturedAt == b.LastCapturedAt && a.Id > b.Id);

    /// <summary>A vendor asset is its row; anything else is its reference, compared without case as the database does.</summary>
    private readonly record struct ObjectKey(VendorScoreSource Source, int ConnectionId, VendorScoreObjectKind Kind,
        long AssetId, string Ref)
    {
        public static ObjectKey Of(VendorScoreSource source, int connection, VendorScoreObjectKind kind, long? assetId,
            string? reference) =>
            new(source, connection, kind, assetId ?? 0, assetId is null ? (reference ?? "").ToUpperInvariant() : "");
    }
}

/// <summary>One reading of a vendor asset, a tenant or an asset group (S40 §7.2 <c>vendor_scores</c>; M57, T271–T273).</summary>
public sealed record VendorObjectReading(VendorScoreSource Source, int ConnectionId, VendorScoreObjectKind ObjectKind,
    long? VendorAssetId, string? ExternalRef, int? EntityId, double Value, double ScaleMax, string? Level, string? Details);
