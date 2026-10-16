using System.Globalization;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// Names the objects of a response (S40 §8.3 <c>RiskIndexObjectLabelDto</c>) under the redaction of §11.3: the owner
/// module's record is read only when the caller holds that module's permission, through the caller's scoped context, so a
/// redacted label never even loads its source row. Everything else is "‹Kind› #ref" with <c>RequiresPermission</c>.
/// </summary>
public static class RiskIndexLabels
{
    public static async Task<Dictionary<IcrObjectKey, RiskIndexObjectLabelDto>> ResolveAsync(NRDbContext db,
        IEnumerable<IcrObjectKey> keys, RiskIndexCaller caller, CancellationToken ct = default)
    {
        var distinct = keys.Distinct().ToList();
        var names = new Dictionary<IcrObjectKey, string>();

        bool May(RiskIndexObjectKind kind) => RiskIndexRedactionPolicy.CanSeeLabel(kind, caller.IsAdmin, caller.Permissions);

        List<int> Ids(RiskIndexObjectKind kind) => distinct.Where(k => k.Kind == kind)
            .Select(k => int.TryParse(k.Ref, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();

        if (May(RiskIndexObjectKind.Scenario) && Ids(RiskIndexObjectKind.Scenario) is { Count: > 0 } risks)
            foreach (var r in await db.Risks.AsNoTracking().Where(r => risks.Contains(r.Id)).Select(r => new { r.Id, r.Subject })
                         .ToListAsync(ct))
                names[Key(RiskIndexObjectKind.Scenario, r.Id)] = r.Subject;

        if (May(RiskIndexObjectKind.Host) && Ids(RiskIndexObjectKind.Host) is { Count: > 0 } hosts)
            foreach (var h in await db.Hosts.AsNoTracking().Where(h => hosts.Contains(h.Id)).Select(h => new { h.Id, h.HostName })
                         .ToListAsync(ct))
                if (!string.IsNullOrWhiteSpace(h.HostName)) names[Key(RiskIndexObjectKind.Host, h.Id)] = h.HostName!;

        if (May(RiskIndexObjectKind.ExternalDomain) && Ids(RiskIndexObjectKind.ExternalDomain) is { Count: > 0 } domains)
            foreach (var c in await db.SecurityScorecardConnections.AsNoTracking().Where(c => domains.Contains(c.Id))
                         .Select(c => new { c.Id, c.Domain }).ToListAsync(ct))
                names[Key(RiskIndexObjectKind.ExternalDomain, c.Id)] = c.Domain;

        if (May(RiskIndexObjectKind.Tenant) && Ids(RiskIndexObjectKind.Tenant) is { Count: > 0 } tenants)
            foreach (var c in await db.TrendMicroConnections.AsNoTracking().Where(c => tenants.Contains(c.Id))
                         .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
                names[Key(RiskIndexObjectKind.Tenant, c.Id)] = c.Name;

        var entities = Ids(RiskIndexObjectKind.Application).Concat(Ids(RiskIndexObjectKind.TargetEntity)).Distinct().ToList();
        if (entities.Count > 0)
        {
            var entityNames = await EntityNamesAsync(db, entities, ct);
            foreach (var key in distinct.Where(k => k.Kind is RiskIndexObjectKind.Application or RiskIndexObjectKind.TargetEntity))
                if (int.TryParse(key.Ref, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                    entityNames.TryGetValue(id, out var name))
                    names[key] = name;
        }

        return distinct.ToDictionary(k => k, k => RiskIndexRedactionPolicy.Label(k.Kind, k.Ref,
            k.Kind == RiskIndexObjectKind.UnassignedGroup ? "Unassigned" : names.GetValueOrDefault(k), caller.IsAdmin,
            caller.Permissions));
    }

    /// <summary>The entity map's names (the <c>name</c> property), visible to every authenticated user (S40 §7.4).</summary>
    public static async Task<Dictionary<int, string>> EntityNamesAsync(NRDbContext db, IReadOnlyCollection<int> ids,
        CancellationToken ct = default)
    {
        var rows = await db.EntitiesProperties.AsNoTracking()
            .Where(p => p.Type == "name" && ids.Contains(p.Entity))
            .Select(p => new { p.Entity, p.Value })
            .ToListAsync(ct);
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).GroupBy(r => r.Entity)
            .ToDictionary(g => g.Key, g => g.First().Value!);
    }

    private static IcrObjectKey Key(RiskIndexObjectKind kind, int id) => new(kind, id.ToString(CultureInfo.InvariantCulture));
}
