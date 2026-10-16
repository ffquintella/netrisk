using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// One stored day as the engine reads it (S39 §9.1, §9.5; S40 §8.1): the object rows of (date, profile) read through the
/// given context — so under its entity scope, which is what makes a partial read exact — with the day's category flags
/// and the current entity map. What derived and partial reads, the preview's replay and the bridge's overlay compose.
/// </summary>
public static class RiskIndexStoredDays
{
    /// <summary>The object rows of the day, without the factor and signal JSON (the engine does not read them).</summary>
    public static async Task<List<RiskIndexObjectRow>> LoadRowsAsync(NRDbContext db, DateOnly date, int profileId,
        CancellationToken ct = default)
    {
        var stored = await db.RiskIndexObjectDays.AsNoTracking()
            .Where(o => o.SnapshotDate == date && o.ProfileId == profileId)
            .Select(o => new
            {
                o.ObjectKind, o.ObjectRef, o.Category, o.Population, o.RValue, o.Weight, o.Criticality, o.CriticalitySource,
                o.CriticalityAuthorId, o.State, o.EntityId, o.Attribution, o.AssetClass, o.SourceMask, o.Environment,
                o.Freshness, o.CoverageEffect, o.RMax, o.ClearedR,
                // A scenario's register inputs, on its REG row only: the seals read them (S39 §6.6).
                ScenarioInputs = o.Category == RiskIndexCategory.Reg ? o.ScenarioInputs : null
            })
            .ToListAsync(ct);

        return stored.Select(o => new RiskIndexObjectRow
        {
            Key = new IcrObjectKey(o.ObjectKind, o.ObjectRef), Category = o.Category, Population = o.Population, R = o.RValue,
            Weight = (double)o.Weight, Criticality = o.Criticality, CriticalitySource = o.CriticalitySource,
            CriticalityAuthorId = o.CriticalityAuthorId, State = o.State, ScopeEntityId = o.EntityId,
            Attribution = RiskIndexStoredJson.ParseAttribution(o.Attribution), AssetClass = o.AssetClass,
            SourceMask = o.SourceMask, Environment = o.Environment, Freshness = o.Freshness, CoverageEffect = o.CoverageEffect,
            RMax = o.RMax, ClearedR = o.ClearedR, ScenarioInputs = RiskIndexStoredJson.ParseScenarioInputs(o.ScenarioInputs)
        }).ToList();
    }

    /// <summary>
    /// Which categories had an enabled source and whether incidents had intake that day. The job writes them as chips on
    /// every category row of the day alike, so any visible row tells; with none visible, they follow the profile's sources.
    /// </summary>
    public static async Task<(IReadOnlyDictionary<RiskIndexCategory, bool> SourceEnabled, bool IntakeActive)> LoadFlagsAsync(
        NRDbContext db, DateOnly date, int profileId, RiskIndexParameters p, CancellationToken ct = default)
    {
        var rows = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate == date && s.ProfileId == profileId && s.Category != RiskIndexCategory.Icr)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Category, s.Chips })
            .Take(64)
            .ToListAsync(ct);

        var flags = FromProfile(p);
        var intake = true;
        foreach (var row in rows.GroupBy(r => r.Category).Select(g => g.First()))
        {
            var chips = RiskIndexStoredJson.ParseChips(row.Chips).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            flags[row.Category] = row.Category != RiskIndexCategory.Ter && !chips.Contains(RiskIndexChipKeys.SourceDisabled) &&
                                  !chips.Contains(RiskIndexChipKeys.NoIncidentIntake);
            if (row.Category == RiskIndexCategory.Ame) intake = !chips.Contains(RiskIndexChipKeys.NoIncidentIntake);
        }

        return (flags, intake);
    }

    /// <summary>The category flags the profile alone implies (the collector's rule, intake assumed active).</summary>
    public static Dictionary<RiskIndexCategory, bool> FromProfile(RiskIndexParameters p)
    {
        var on = p.EnabledSources.ToHashSet(StringComparer.Ordinal);
        var exposure = new[]
        {
            RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.SscTotal, RiskIndexSources.TenableNessus,
            RiskIndexSources.TenableVm, RiskIndexSources.TenableAes, RiskIndexSources.Importers
        }.Any(on.Contains);
        return new Dictionary<RiskIndexCategory, bool>
        {
            [RiskIndexCategory.Reg] = on.Contains(RiskIndexSources.RegResidual),
            [RiskIndexCategory.Exp] = exposure,
            [RiskIndexCategory.Ame] = on.Contains(RiskIndexSources.Incidents) || on.Contains(RiskIndexSources.NearMisses),
            [RiskIndexCategory.Ctl] = on.Contains(RiskIndexSources.Assessments) || on.Contains(RiskIndexSources.IncidentResponsePlans),
            [RiskIndexCategory.Gov] = on.Contains(RiskIndexSources.RegResidual) || exposure,
            [RiskIndexCategory.Ter] = false
        };
    }

    /// <summary>The day as an object set the engine composes.</summary>
    public static async Task<RiskIndexObjectSet> LoadSetAsync(NRDbContext db, DateOnly date, int profileId,
        RiskIndexParameters p, EntityMap map, CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(db, date, profileId, ct);
        var (flags, intake) = await LoadFlagsAsync(db, date, profileId, p, ct);
        return new RiskIndexObjectSet { Rows = rows, SourceEnabled = flags, IncidentIntakeActive = intake, Map = map };
    }
}
