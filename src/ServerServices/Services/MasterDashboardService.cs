using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Dashboard;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

/// <summary>
/// Builds the cross-entity Master Dashboard (Track 2 milestone 2.3.3).
///
/// The spec is explicit that the rollup is computed server-side in one pass — a client
/// looping "per entity" would be an N+1 against a table set that grows with the tenant
/// count. So each of the three fact tables is grouped by <c>entity_id</c> exactly once and
/// the groups are then stitched onto the entity list in memory.
/// </summary>
public class MasterDashboardService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IMasterDashboardService
{
    /// <summary>
    /// How long a computed rollup is reused. The spec calls for 1–5 minutes: the dashboard is a
    /// posture overview, not a live console, and the grouped scans are the most expensive read
    /// an admin can trigger by holding down F5.
    /// </summary>
    private static readonly TimeSpan CacheWindow = TimeSpan.FromMinutes(2);

    // Instance state, not static: the service is registered as a singleton, so one instance per
    // process already gives a process-wide cache — while keeping tests (which build their own
    // container per class) isolated from each other.
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private MasterDashboard? _cached;
    private DateTime _cachedAt = DateTime.MinValue;

    /// <summary>
    /// Statuses that take a vulnerability or an incident out of the open population. Everything
    /// not listed counts as open, so a status added later shows up on the dashboard as work
    /// outstanding rather than silently vanishing.
    /// </summary>
    private static readonly HashSet<int> ClosedStatuses =
    [
        (int)IntStatus.Closed,
        (int)IntStatus.NotRelevant,
        (int)IntStatus.Rejected,
        (int)IntStatus.Duplicated,
        (int)IntStatus.Fixed,
        (int)IntStatus.Solved,
        (int)IntStatus.Retired,
        (int)IntStatus.Deleted,
        (int)IntStatus.Completed,
        (int)IntStatus.Cancelled
    ];

    /// <summary>
    /// Entity definitions that represent a business entity for multi-tenant scoping. Entities of
    /// any other definition (people, applications, data groups) only earn a card when they carry
    /// open work of their own.
    /// </summary>
    private static readonly string[] OrganizationalDefinitions =
        ["organization", "organizationUnit", "subOrganizationUnit"];

    /// <summary>Dictionary key standing in for a null <c>entity_id</c>; 0 is never a real entity id.</summary>
    private const int UnassignedKey = 0;

    public async Task<MasterDashboard> GetMasterDashboardAsync(bool useCache = true)
    {
        if (useCache)
        {
            var hit = ReadCache();
            if (hit != null) return hit;
        }

        await _cacheLock.WaitAsync();
        try
        {
            // Re-check inside the lock: several admins hitting Refresh at once should produce
            // one scan, not one per caller.
            if (useCache)
            {
                var hit = ReadCache();
                if (hit != null) return hit;
            }

            var dashboard = await ComputeAsync();
            _cached = dashboard;
            _cachedAt = DateTime.UtcNow;

            // Hand back a copy so a caller mutating the result cannot poison the cache.
            return Clone(dashboard, fromCache: false);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private MasterDashboard? ReadCache()
    {
        var snapshot = _cached;
        if (snapshot == null) return null;
        if (DateTime.UtcNow - _cachedAt > CacheWindow) return null;
        return Clone(snapshot, fromCache: true);
    }

    private async Task<MasterDashboard> ComputeAsync()
    {
        await using var dbContext = DalService.GetContext();

        var riskGroups = await ComputeRiskGroupsAsync(dbContext);
        var vulnGroups = await ComputeVulnerabilityGroupsAsync(dbContext);
        var incidentGroups = await ComputeIncidentGroupsAsync(dbContext);

        // Cards are drawn for two populations: every organisational entity (so a unit with a
        // clean sheet still reports zero rather than disappearing), plus any other entity that
        // actually carries open work. Deriving the second set from the data avoids hard-coding
        // which of the ~15 entity definitions a deployment happens to scope its risks by.
        var idsWithData = riskGroups.Keys
            .Concat(vulnGroups.Keys)
            .Concat(incidentGroups.Keys)
            .Where(id => id != UnassignedKey)
            .Distinct()
            .ToList();

        var entities = await dbContext.Entities
            .AsNoTracking()
            .Where(e => OrganizationalDefinitions.Contains(e.DefinitionName) || idsWithData.Contains(e.Id))
            .Select(e => new { e.Id, e.DefinitionName, e.Status })
            .ToListAsync();

        // The display name lives in the EAV-style properties table, not on the entity row.
        var entityIds = entities.Select(e => e.Id).ToList();
        var names = await dbContext.EntitiesProperties
            .AsNoTracking()
            .Where(p => entityIds.Contains(p.Entity) && p.Type == "name")
            .Select(p => new { p.Entity, p.Value })
            .ToListAsync();
        var nameByEntity = names
            .GroupBy(n => n.Entity)
            .ToDictionary(g => g.Key, g => g.First().Value);

        var summaries = entities
            .Select(entity => BuildSummary(
                entity.Id,
                nameByEntity.GetValueOrDefault(entity.Id) ?? $"Entity {entity.Id}",
                entity.Status,
                riskGroups,
                vulnGroups,
                incidentGroups))
            .ToList();

        // Records still awaiting the 2.3.1 backfill would otherwise be invisible here while
        // still counting on the per-module screens, so the totals would not reconcile.
        var hasUnassigned = riskGroups.ContainsKey(UnassignedKey)
                            || vulnGroups.ContainsKey(UnassignedKey)
                            || incidentGroups.ContainsKey(UnassignedKey);
        if (hasUnassigned)
        {
            summaries.Add(BuildSummary(null, "Unassigned", null, riskGroups, vulnGroups, incidentGroups));
        }

        var dashboard = new MasterDashboard
        {
            GeneratedAt = DateTime.UtcNow,
            FromCache = false,
            Entities = summaries.OrderByDescending(s => s.PostureScore).ThenBy(s => s.EntityName).ToList(),
            Totals = BuildTotals(summaries)
        };

        Logger.Information("Master dashboard computed for {EntityCount} entities", dashboard.Entities.Count);

        return dashboard;
    }

    /// <summary>
    /// Open risks grouped by entity. Risk severity bands mirror <c>StatisticsService</c>
    /// (&gt;7 high, &gt;4 medium, else low) so the two screens agree.
    /// </summary>
    private static async Task<Dictionary<int, RiskRollup>> ComputeRiskGroupsAsync(DAL.Context.AuditableContext dbContext)
    {
        var rows = await dbContext.Risks
            .AsNoTracking()
            .Where(r => r.StatusId != RiskStatus.Closed)
            .Join(dbContext.RiskScorings.AsNoTracking(),
                risk => risk.Id,
                scoring => scoring.Id,
                (risk, scoring) => new { EntityId = risk.EntityId, scoring.CalculatedRisk })
            .ToListAsync();

        return rows
            .GroupBy(r => r.EntityId ?? UnassignedKey)
            .ToDictionary(g => g.Key, g => new RiskRollup
            {
                Open = g.Count(),
                High = g.Count(r => r.CalculatedRisk > 7),
                Medium = g.Count(r => r.CalculatedRisk is > 4 and <= 7),
                Low = g.Count(r => r.CalculatedRisk <= 4),
                AverageScore = g.Average(r => (double)r.CalculatedRisk)
            });
    }

    /// <summary>Open vulnerabilities grouped by entity. Severity is the legacy "0".."4" string.</summary>
    private static async Task<Dictionary<int, VulnerabilityRollup>> ComputeVulnerabilityGroupsAsync(
        DAL.Context.AuditableContext dbContext)
    {
        var rows = await dbContext.Vulnerabilities
            .AsNoTracking()
            .Select(v => new { v.EntityId, v.Status, v.Severity })
            .ToListAsync();

        return rows
            .Where(v => !ClosedStatuses.Contains(v.Status))
            .GroupBy(v => v.EntityId ?? UnassignedKey)
            .ToDictionary(g => g.Key, g => new VulnerabilityRollup
            {
                Open = g.Count(),
                Critical = g.Count(v => v.Severity == "4"),
                High = g.Count(v => v.Severity == "3"),
                Medium = g.Count(v => v.Severity == "2"),
                Low = g.Count(v => v.Severity == "1")
            });
    }

    /// <summary>Open incidents grouped by entity.</summary>
    private static async Task<Dictionary<int, int>> ComputeIncidentGroupsAsync(DAL.Context.AuditableContext dbContext)
    {
        var rows = await dbContext.Incidents
            .AsNoTracking()
            .Select(i => new { i.EntityId, i.Status })
            .ToListAsync();

        return rows
            .Where(i => !ClosedStatuses.Contains(i.Status))
            .GroupBy(i => i.EntityId ?? UnassignedKey)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static EntityPostureSummary BuildSummary(
        int? entityId,
        string name,
        string? status,
        IReadOnlyDictionary<int, RiskRollup> riskGroups,
        IReadOnlyDictionary<int, VulnerabilityRollup> vulnGroups,
        IReadOnlyDictionary<int, int> incidentGroups)
    {
        var key = entityId ?? UnassignedKey;
        var risks = riskGroups.GetValueOrDefault(key) ?? new RiskRollup();
        var vulns = vulnGroups.GetValueOrDefault(key) ?? new VulnerabilityRollup();
        var incidents = incidentGroups.GetValueOrDefault(key);

        var summary = new EntityPostureSummary
        {
            EntityId = entityId,
            EntityName = name,
            EntityStatus = status,
            OpenRisks = risks.Open,
            RisksHigh = risks.High,
            RisksMedium = risks.Medium,
            RisksLow = risks.Low,
            AverageRiskScore = Math.Round(risks.AverageScore, 2),
            OpenVulnerabilities = vulns.Open,
            VulnerabilitiesCritical = vulns.Critical,
            VulnerabilitiesHigh = vulns.High,
            VulnerabilitiesMedium = vulns.Medium,
            VulnerabilitiesLow = vulns.Low,
            OpenIncidents = incidents
        };

        summary.PostureScore = CalculatePostureScore(summary);
        return summary;
    }

    /// <summary>
    /// Weighted 0–100 triage indicator used only to order the cards: high-severity work weighs
    /// more than low, and an open incident weighs most because it is happening now. Saturates at
    /// 100 rather than growing without bound so one huge entity cannot flatten the rest.
    /// </summary>
    private static double CalculatePostureScore(EntityPostureSummary s)
    {
        var weighted =
            s.RisksHigh * 5.0 + s.RisksMedium * 2.0 + s.RisksLow * 0.5 +
            s.VulnerabilitiesCritical * 4.0 + s.VulnerabilitiesHigh * 2.0 +
            s.VulnerabilitiesMedium * 0.75 + s.VulnerabilitiesLow * 0.25 +
            s.OpenIncidents * 8.0;

        if (weighted <= 0) return 0;

        // Diminishing returns: 100 * (1 - e^-x/40). 40 weighted points lands around 63.
        return Math.Round(100.0 * (1.0 - Math.Exp(-weighted / 40.0)), 1);
    }

    private static EntityPostureSummary BuildTotals(IEnumerable<EntityPostureSummary> summaries)
    {
        var list = summaries.ToList();

        var totals = new EntityPostureSummary
        {
            EntityId = null,
            EntityName = "All entities",
            OpenRisks = list.Sum(s => s.OpenRisks),
            RisksHigh = list.Sum(s => s.RisksHigh),
            RisksMedium = list.Sum(s => s.RisksMedium),
            RisksLow = list.Sum(s => s.RisksLow),
            OpenVulnerabilities = list.Sum(s => s.OpenVulnerabilities),
            VulnerabilitiesCritical = list.Sum(s => s.VulnerabilitiesCritical),
            VulnerabilitiesHigh = list.Sum(s => s.VulnerabilitiesHigh),
            VulnerabilitiesMedium = list.Sum(s => s.VulnerabilitiesMedium),
            VulnerabilitiesLow = list.Sum(s => s.VulnerabilitiesLow),
            OpenIncidents = list.Sum(s => s.OpenIncidents)
        };

        // Weight each entity's mean by its open-risk count, otherwise an entity with one risk
        // would pull the organisation average as hard as one with a thousand.
        var scored = list.Where(s => s.OpenRisks > 0).ToList();
        totals.AverageRiskScore = scored.Count == 0
            ? 0
            : Math.Round(scored.Sum(s => s.AverageRiskScore * s.OpenRisks) / scored.Sum(s => s.OpenRisks), 2);

        totals.PostureScore = CalculatePostureScore(totals);
        return totals;
    }

    private static MasterDashboard Clone(MasterDashboard source, bool fromCache) => new()
    {
        GeneratedAt = source.GeneratedAt,
        FromCache = fromCache,
        Totals = Clone(source.Totals),
        Entities = source.Entities.Select(Clone).ToList()
    };

    private static EntityPostureSummary Clone(EntityPostureSummary s) => new()
    {
        EntityId = s.EntityId,
        EntityName = s.EntityName,
        EntityStatus = s.EntityStatus,
        OpenRisks = s.OpenRisks,
        RisksHigh = s.RisksHigh,
        RisksMedium = s.RisksMedium,
        RisksLow = s.RisksLow,
        AverageRiskScore = s.AverageRiskScore,
        OpenVulnerabilities = s.OpenVulnerabilities,
        VulnerabilitiesCritical = s.VulnerabilitiesCritical,
        VulnerabilitiesHigh = s.VulnerabilitiesHigh,
        VulnerabilitiesMedium = s.VulnerabilitiesMedium,
        VulnerabilitiesLow = s.VulnerabilitiesLow,
        OpenIncidents = s.OpenIncidents,
        PostureScore = s.PostureScore
    };

    private sealed class RiskRollup
    {
        public int Open { get; init; }
        public int High { get; init; }
        public int Medium { get; init; }
        public int Low { get; init; }
        public double AverageScore { get; init; }
    }

    private sealed class VulnerabilityRollup
    {
        public int Open { get; init; }
        public int Critical { get; init; }
        public int High { get; init; }
        public int Medium { get; init; }
        public int Low { get; init; }
    }
}
