using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex;

public static partial class RiskIndexObjectCollector
{
    /// <summary>The Vision One vendor-asset sources of S39 §3.2 (M57, T272).</summary>
    private static readonly string[] VendorAssetSources =
    [
        RiskIndexSources.V1InternetFacing, RiskIndexSources.V1Accounts, RiskIndexSources.V1Cloud, RiskIndexSources.V1LocalApps
    ];

    /// <summary>
    /// What a vendor asset scores (S39 §3.2): FQDNs and public IPs are V1-IFA in EXP.X, accounts V1-ACC in EXP.I, cloud
    /// assets V1-CLD and local apps V1-APP in EXP.N, each <c>s/100</c> of its Vision One risk score.
    /// </summary>
    internal static (string Source, string Factor, RiskIndexAssetClass AssetClass) VendorAssetScoring(VendorAssetKind kind) =>
        kind switch
        {
            VendorAssetKind.Fqdn or VendorAssetKind.PublicIp =>
                (RiskIndexSources.V1InternetFacing, RiskIndexFactors.ExpExternal, RiskIndexAssetClass.InternetFacing),
            VendorAssetKind.DomainAccount or VendorAssetKind.ServiceAccount =>
                (RiskIndexSources.V1Accounts, RiskIndexFactors.ExpIdentities, RiskIndexAssetClass.Identity),
            VendorAssetKind.CloudAsset => (RiskIndexSources.V1Cloud, RiskIndexFactors.ExpCloud, RiskIndexAssetClass.Cloud),
            _ => (RiskIndexSources.V1LocalApps, RiskIndexFactors.ExpCloud, RiskIndexAssetClass.Application)
        };

    /// <summary>
    /// One EXP object per active Vision One vendor asset whose source the profile enables (M57, T272): the current
    /// <c>V1VendorAsset</c> reading as a C factor, read at its last capture; without one the asset is presumed. Criticality
    /// is the vendor's, capped at <c>crit_max_vision_one</c> like a host's (S39 §5.1), else the default.
    /// </summary>
    private static async Task CollectVendorAssetsAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        var kinds = Enum.GetValues<VendorAssetKind>().Where(k => run.On(VendorAssetScoring(k).Source)).ToList();
        if (kinds.Count == 0) return;

        var assets = await db.VendorAssets.AsNoTracking()
            .Where(a => a.Provider == IntegrationKind.TrendMicroVisionOne && a.Status == VendorAssetStatus.Active &&
                        kinds.Contains(a.AssetKind) && a.FirstSeenAt <= run.Now)
            .Select(a => new { a.Id, a.AssetKind, a.Criticality, a.EntityId })
            .ToListAsync(ct);
        if (assets.Count == 0) return;

        var wanted = assets.Select(a => a.Id).ToHashSet();
        var latest = (await db.VendorScores.AsNoTracking()
                .Where(v => v.Source == VendorScoreSource.V1VendorAsset && v.VendorAssetId != null && v.FirstCapturedAt <= run.Now)
                .Select(v => new { v.Id, AssetId = v.VendorAssetId!.Value, v.Value, v.LastCapturedAt })
                .ToListAsync(ct))
            .Where(v => wanted.Contains(v.AssetId))
            .GroupBy(v => v.AssetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.LastCapturedAt).ThenByDescending(v => v.Id).First());

        foreach (var a in assets.OrderBy(a => a.Id))
        {
            var (source, factor, assetClass) = VendorAssetScoring(a.AssetKind);
            var crit = a.Criticality is { } c
                ? Math.Min(Math.Clamp(c, 1, 5), run.P.VisionOneCriticalityCeiling)
                : run.P.DefaultCriticality;
            var m = run.M(crit);
            var u = run.U(factor);

            var readings = new List<Reading>();
            if (latest.TryGetValue(a.Id, out var score))
            {
                var readAt = score.LastCapturedAt > run.Now ? run.Now : score.LastCapturedAt;
                var x = IcrMath.Clamp01(score.Value / 100.0);
                readings.Add(new Reading(factor, RiskIndexFactorType.C, x,
                    IcrMath.Validity(IcrMath.Days(readAt, run.Now), run.W(source)), u, readAt, source,
                    [new RiskIndexSignalReading(factor, $"v1-asset:{a.Id}", x, [])]));
            }

            var key = new IcrObjectKey(RiskIndexObjectKind.VendorAsset, a.Id.ToString(CultureInfo.InvariantCulture));
            run.Rows.Add(ExposureRow(run, key, readings, m, u, u) with
            {
                Criticality = crit, CriticalitySource = a.Criticality is null ? "default" : CriticalitySources.VisionOne,
                ScopeEntityId = a.EntityId, Attribution = a.EntityId is { } e ? [e] : [], AssetClass = assetClass
            });
        }
    }

    /// <summary>
    /// CTL.C from V1-SCS (S39 §3.2; M57, T271): one Tenant object per enabled Vision One connection that syncs its posture,
    /// valued <c>u_C·(1 − adoption)</c> from its current <c>V1SecurityConfig</c> reading; presumed without one.
    /// </summary>
    private static async Task CollectSecurityConfigurationAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        if (!run.On(RiskIndexSources.V1SecurityConfiguration)) return;

        var connections = await db.TrendMicroConnections.AsNoTracking().Where(c => c.Enabled && c.SyncPosture)
            .Select(c => new { c.Id, c.EntityId }).ToListAsync(ct);
        if (connections.Count == 0) return;

        var ids = connections.Select(c => c.Id).ToList();
        var latest = (await db.VendorScores.AsNoTracking()
                .Where(v => v.Source == VendorScoreSource.V1SecurityConfig && v.ObjectKind == VendorScoreObjectKind.Tenant &&
                            ids.Contains(v.ConnectionId) && v.FirstCapturedAt <= run.Now)
                .Select(v => new { v.Id, v.ConnectionId, v.Value, v.LastCapturedAt })
                .ToListAsync(ct))
            .GroupBy(v => v.ConnectionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.LastCapturedAt).ThenByDescending(v => v.Id).First());

        var crit = Math.Clamp(run.P.DefaultCriticality, 1, 5);
        var m = run.M(crit);
        var u = run.U(RiskIndexFactors.CtlConfiguration);
        var window = run.W(RiskIndexSources.V1SecurityConfiguration);

        foreach (var c in connections.OrderBy(c => c.Id))
        {
            var readings = new List<Reading>();
            if (latest.TryGetValue(c.Id, out var score))
            {
                var readAt = score.LastCapturedAt > run.Now ? run.Now : score.LastCapturedAt;
                var x = IcrMath.Clamp01(1 - score.Value);
                readings.Add(new Reading(RiskIndexFactors.CtlConfiguration, RiskIndexFactorType.C, x,
                    IcrMath.Validity(IcrMath.Days(readAt, run.Now), window), u, readAt,
                    RiskIndexSources.V1SecurityConfiguration,
                    [new RiskIndexSignalReading(RiskIndexFactors.CtlConfiguration, "adoption", x, [])]));
            }

            var key = new IcrObjectKey(RiskIndexObjectKind.Tenant, c.Id.ToString(CultureInfo.InvariantCulture));
            run.Rows.Add(ExposureRow(run, key, readings, m, u, u) with
            {
                Category = RiskIndexCategory.Ctl, Population = RiskIndexPopulation.Tenants, Criticality = crit,
                CriticalitySource = "default", ScopeEntityId = c.EntityId, Attribution = c.EntityId is { } e ? [e] : []
            });
        }
    }
}
