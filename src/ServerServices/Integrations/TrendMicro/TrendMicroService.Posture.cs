using System.Text.Json;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;

namespace ServerServices.Integrations.TrendMicro;

/// <summary>
/// M57, T271–T273 (S40 §6.9, §9.5): the posture phases of a Vision One sync — the tenant's CRI, configuration adoption and
/// asset groups as reference indicators, the attack-surface objects as scored vendor assets, and the Workbench alerts as
/// the attack-intensity indicator. Each phase is opted into on the connection and fails on its own.
/// </summary>
public partial class TrendMicroService
{
    /// <summary>The <c>external_ref</c> of a tenant-level reading (one per connection).</summary>
    public const string TenantRef = "tenant";

    /// <summary>The Workbench window the attack intensity counts (S40 §9.5: 30 days).</summary>
    public const int WorkbenchWindowDays = 30;

    private async Task SyncPostureExpansionAsync(TrendMicroConnection connection, string? apiKey,
        IReadOnlyList<MappingRule> tagRules, IntegrationSyncRun run, PostureSyncResult result, CancellationToken ct)
    {
        if (connection.SyncPosture) await SyncPostureIndicatorsAsync(connection, apiKey, run, result, ct);
        if (connection.SyncAttackSurfaceObjects) await SyncAttackSurfaceAsync(connection, apiKey, tagRules, run, result, ct);
        if (connection.SyncWorkbenchAlerts) await SyncWorkbenchAsync(connection, apiKey, run, result, ct);
    }

    // --- T271: securityPosture and assetGroups ---------------------------------------------------------------------------

    /// <summary>
    /// The tenant's CRI (V1Cri, 0–100, category levels in <c>details</c>), its mean agent-feature adoption (V1SecurityConfig,
    /// 0–1, for CTL.C) and each asset group's risk (V1AssetGroup) as piecewise-constant readings. A group's reading carries
    /// the entity of the asset-group rule that names it (by id or name), else the connection's: the group's subindex is a
    /// reference indicator of that node and attributes no device (S40 §6.6).
    /// </summary>
    private async Task SyncPostureIndicatorsAsync(TrendMicroConnection connection, string? apiKey, IntegrationSyncRun run,
        PostureSyncResult result, CancellationToken ct)
    {
        var readings = new List<VendorObjectReading>();

        try
        {
            await run.StepAsync("posture", "Requesting the security posture.", ct: ct);
            var posture = await client.GetSecurityPostureAsync(connection, apiKey, ct);

            if (posture?.RiskIndex is { } cri)
                readings.Add(new VendorObjectReading(VendorScoreSource.V1Cri, connection.Id, VendorScoreObjectKind.Tenant,
                    null, TenantRef, connection.EntityId, cri, 100, Truncate(posture.RiskLevel, 16), posture.CategoriesJson));

            if (posture?.MeanAdoption is { } adoption)
                readings.Add(new VendorObjectReading(VendorScoreSource.V1SecurityConfig, connection.Id,
                    VendorScoreObjectKind.Tenant, null, TenantRef, connection.EntityId, adoption, 1, null,
                    JsonSerializer.Serialize(posture.FeatureAdoption.Select(f => new { feature = f.Feature, adoptionRate = f.AdoptionRate }))));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordPhaseError(connection, result, "posture", ex);
        }

        try
        {
            await run.StepAsync("posture", "Requesting the asset groups.", ct: ct);
            var groups = await client.GetAssetGroupsAsync(connection, apiKey,
                message => run.StepAsync("posture", message, ct: ct), ct);
            var rules = await AssetGroupRulesAsync(connection, ct);

            foreach (var group in groups.Where(g => g.RiskScore is not null))
            {
                var rule = rules.FirstOrDefault(r => Matches(r.Value, group.Id) || Matches(r.Value, group.Name));
                readings.Add(new VendorObjectReading(VendorScoreSource.V1AssetGroup, connection.Id,
                    VendorScoreObjectKind.AssetGroup, null, Truncate(group.Id, 128), rule.Id == 0 ? connection.EntityId : rule.EntityId,
                    group.RiskScore!.Value, 100, Truncate(group.RiskLevel, 16),
                    JsonSerializer.Serialize(new { name = group.Name, assetCount = group.AssetCount, mappingId = rule.Id == 0 ? (int?)null : rule.Id })));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordPhaseError(connection, result, "asset groups", ex);
        }

        await WriteReadingsAsync(readings, ct);
        result.IndicatorsWritten += readings.Count;

        await run.StepAsync("posture", $"{readings.Count} posture and asset-group reading(s) recorded.", ct: ct);
    }

    private static bool Matches(string ruleValue, string? candidate) =>
        candidate is not null && string.Equals(ruleValue, VendorTagText.Canonical(candidate), StringComparison.OrdinalIgnoreCase);

    /// <summary>The connection's enabled asset-group rules, in priority order (T263 stored them inert until this task).</summary>
    private async Task<List<(int Id, string Value, int EntityId)>> AssetGroupRulesAsync(TrendMicroConnection connection,
        CancellationToken ct)
    {
        await using var db = DalService.GetContext();
        return (await db.VendorEntityMappings.AsNoTracking()
                .Where(m => m.Provider == IntegrationKind.TrendMicroVisionOne && m.ConnectionId == connection.Id &&
                            m.MatchKind == VendorMatchKind.V1AssetGroup && m.Enabled)
                .OrderBy(m => m.Priority).ThenBy(m => m.Id)
                .Select(m => new { m.Id, m.MatchValue, m.EntityId })
                .ToListAsync(ct))
            .Select(m => (m.Id, m.MatchValue, m.EntityId))
            .ToList();
    }

    // --- T272: attack-surface objects --------------------------------------------------------------------------------------

    /// <summary>
    /// Each attack-surface endpoint into <c>vendor_assets</c> and its risk score into <c>vendor_scores</c> (V1VendorAsset).
    /// A kind whose read fails keeps its assets as they were; a kind read completely marks the assets it no longer lists
    /// <c>Gone</c>. A new asset takes the winning tag rule's entity when the connection syncs tags, else the connection's.
    /// </summary>
    private async Task SyncAttackSurfaceAsync(TrendMicroConnection connection, string? apiKey,
        IReadOnlyList<MappingRule> tagRules, IntegrationSyncRun run, PostureSyncResult result, CancellationToken ct)
    {
        foreach (var kind in Enum.GetValues<VendorAssetKind>())
        {
            try
            {
                await run.StepAsync("attack-surface", $"Requesting {kind} assets.", ct: ct);
                var assets = await client.GetAttackSurfaceAssetsAsync(connection, apiKey, kind,
                    message => run.StepAsync("attack-surface", message, ct: ct), ct);
                await UpsertVendorAssetsAsync(connection, kind, assets, tagRules, result, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordPhaseError(connection, result, $"{kind} assets", ex);
            }
        }

        await run.StepAsync("attack-surface",
            $"{result.VendorAssetsSynced} attack-surface asset(s) recorded, {result.VendorAssetsGone} gone.", ct: ct);
    }

    private async Task UpsertVendorAssetsAsync(TrendMicroConnection connection, VendorAssetKind kind,
        List<TrendMicroAttackSurfaceAsset> assets, IReadOnlyList<MappingRule> tagRules, PostureSyncResult result,
        CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        var known = new Dictionary<string, VendorAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in await db.VendorAssets
                     .Where(a => a.Provider == IntegrationKind.TrendMicroVisionOne && a.ConnectionId == connection.Id &&
                                 a.AssetKind == kind)
                     .ToListAsync(ct))
            known.TryAdd(row.ExternalId, row);

        var now = DateTime.UtcNow;
        var seen = new Dictionary<string, (VendorAsset Row, TrendMicroAttackSurfaceAsset Asset)>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            var externalId = Truncate(asset.Id, 255)!;
            if (seen.ContainsKey(externalId)) continue;

            var tags = !connection.SyncCustomTags || asset.Tags.Count == 0
                ? null
                : JsonSerializer.Serialize(asset.Tags.Order(StringComparer.OrdinalIgnoreCase).ToList());
            var winner = connection.SyncCustomTags ? VendorEntityMappingRules.Winner(tagRules, asset.Tags) : null;

            if (!known.TryGetValue(externalId, out var row))
            {
                row = new VendorAsset
                {
                    Provider = IntegrationKind.TrendMicroVisionOne, ConnectionId = connection.Id, AssetKind = kind,
                    ExternalId = externalId, EntityId = winner?.EntityId ?? connection.EntityId,
                    FirstSeenAt = asset.FirstSeen ?? now, CreatedAt = now
                };
                db.VendorAssets.Add(row);
                known[externalId] = row;
            }
            else
            {
                if (winner is not null) row.EntityId = VendorEntityMappingRules.Apply(row.EntityId, winner);
                if (asset.FirstSeen is { } first) row.FirstSeenAt = first;
                row.UpdatedAt = now;
            }

            row.Name = Truncate(asset.Name, 255)!;
            row.Criticality = asset.Criticality;
            row.CriticalityRaw = Truncate(asset.CriticalityRaw, 32);
            if (connection.SyncCustomTags) row.Tags = tags;
            row.LastSeenAt = asset.LastDetected ?? now;
            row.Status = VendorAssetStatus.Active;

            seen[externalId] = (row, asset);
        }

        foreach (var row in known.Values.Where(r => r.Status == VendorAssetStatus.Active && !seen.ContainsKey(r.ExternalId)))
        {
            row.Status = VendorAssetStatus.Gone;
            row.UpdatedAt = now;
            result.VendorAssetsGone++;
        }

        await db.SaveChangesAsync(ct);

        var readings = seen.Values.Where(v => v.Asset.RiskScore is not null)
            .Select(v => new VendorObjectReading(VendorScoreSource.V1VendorAsset, connection.Id,
                VendorScoreObjectKind.VendorAsset, v.Row.Id, Truncate(v.Row.ExternalId, 128), v.Row.EntityId,
                v.Asset.RiskScore!.Value, 100, Truncate(v.Asset.RiskLevel, 16), null))
            .ToList();

        await VendorScoreStore.ApplyObjectsAsync(db, readings, now, ct);
        await db.SaveChangesAsync(ct);

        result.VendorAssetsSynced += seen.Count;
    }

    // --- T273: Workbench alerts --------------------------------------------------------------------------------------------

    /// <summary>
    /// The alerts of the last 30 days counted per day and severity into one tenant reading (V1AttackIntensity): the value is
    /// the 30-day count and <c>details</c> holds the breakdown. An indicator beside the index, never a scored factor — a raw
    /// alert is not a risk (S39 §3.2 V1-WB). <c>scale_max</c> is 0: a count has no top.
    /// </summary>
    private async Task SyncWorkbenchAsync(TrendMicroConnection connection, string? apiKey, IntegrationSyncRun run,
        PostureSyncResult result, CancellationToken ct)
    {
        try
        {
            var to = DateTime.UtcNow;
            var from = to.AddDays(-WorkbenchWindowDays);

            await run.StepAsync("workbench", $"Requesting the Workbench alerts of the last {WorkbenchWindowDays} days.", ct: ct);
            var alerts = (await client.GetWorkbenchAlertsAsync(connection, apiKey, from, to,
                    message => run.StepAsync("workbench", message, ct: ct), ct))
                .Where(a => a.CreatedAt >= from && a.CreatedAt <= to)
                .DistinctBy(a => a.Id)
                .ToList();

            static string Severity(TrendMicroWorkbenchAlert a) => string.IsNullOrWhiteSpace(a.Severity) ? "unknown" : a.Severity!;

            var details = JsonSerializer.Serialize(new
            {
                windowDays = WorkbenchWindowDays,
                from,
                to,
                total = alerts.Count,
                bySeverity = alerts.GroupBy(Severity).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                byDay = alerts.GroupBy(a => a.CreatedAt.Date).OrderBy(g => g.Key).Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    counts = g.GroupBy(Severity).OrderBy(s => s.Key).ToDictionary(s => s.Key, s => s.Count())
                })
            });

            await WriteReadingsAsync(
            [
                new VendorObjectReading(VendorScoreSource.V1AttackIntensity, connection.Id, VendorScoreObjectKind.Tenant,
                    null, TenantRef, connection.EntityId, alerts.Count, 0, null, details)
            ], ct);

            result.WorkbenchAlerts += alerts.Count;
            result.IndicatorsWritten++;

            await run.StepAsync("workbench", $"{alerts.Count} alert(s) counted as the attack intensity.", ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordPhaseError(connection, result, "Workbench alerts", ex);
        }
    }

    // --- shared -------------------------------------------------------------------------------------------------------------

    private async Task WriteReadingsAsync(IReadOnlyList<VendorObjectReading> readings, CancellationToken ct)
    {
        if (readings.Count == 0) return;
        await using var db = DalService.GetContext();
        await VendorScoreStore.ApplyObjectsAsync(db, readings, DateTime.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A phase that fails is counted and named, and the sync goes on to the next one (a partial success).</summary>
    private void RecordPhaseError(TrendMicroConnection connection, PostureSyncResult result, string phase, Exception ex)
    {
        result.Errors++;
        result.Messages.Add($"Vision One {phase}: {ex.Message}");
        Logger.Warning("Vision One {Phase} for connection {Connection} failed: {Message}", phase, connection.Name, ex.Message);
    }
}
