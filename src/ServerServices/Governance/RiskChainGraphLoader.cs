using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Tools.Risks;

namespace ServerServices.Governance;

/// <summary>
/// Loads the chain nodes and the property rows that carry their edges into a <see cref="RiskChainGraph"/>.
///
/// Two queries, whole-table over the chain types: edges are entity-property values stored as text, so
/// there is nothing to join on in SQL. That is fine at the ~500 entities S40 §7.7 estimates; a typed
/// edge table is the answer if that grows by two orders of magnitude (S41 §11, R1).
/// </summary>
public static class RiskChainGraphLoader
{
    public static async Task<RiskChainGraph> LoadAsync(AuditableContext db)
    {
        var chainTypes = RiskChainSchema.ChainDefinitions.ToList();
        var properties = RiskChainSchema.ReadProperties.ToList();

        var nodes = await db.Entities.AsNoTracking()
            .Where(e => chainTypes.Contains(e.DefinitionName))
            .Select(e => new { e.Id, e.DefinitionName, e.Parent })
            .ToListAsync();

        var rows = await db.EntitiesProperties.AsNoTracking()
            .Where(p => properties.Contains(p.Type) && chainTypes.Contains(p.EntityNavigation.DefinitionName))
            .Select(p => new { p.Entity, p.Type, p.Value })
            .ToListAsync();

        var byEntity = rows
            .GroupBy(r => r.Entity)
            .ToDictionary(g => g.Key,
                g => g.Select(r => new KeyValuePair<string, string>(r.Type, r.Value)).ToList());

        return new RiskChainGraph(nodes.Select(n => new RiskChainNode(n.Id, n.DefinitionName, n.Parent,
            byEntity.GetValueOrDefault(n.Id))));
    }
}
