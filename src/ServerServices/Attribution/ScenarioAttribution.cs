using DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace ServerServices.Attribution;

/// <summary>One risk scenario attributed to one entity (S39 §10.2).</summary>
/// <remarks>
/// A record so that two attributions with the same pair are equal by value: a relational provider
/// deduplicates the union in SQL, but a provider that evaluates the union in memory compares the
/// projected objects, and with reference equality it would keep both copies of a pair.
/// </remarks>
public sealed record RiskEntityAttribution
{
    public int RiskId { get; init; }
    public int EntityId { get; init; }
}

/// <summary>
/// Which entities a risk scenario belongs to (S39 §10.2, D-05, T240).
///
/// The register holds two links from a risk to entities: <c>risks.entity_id</c>, the scope column
/// that visibility, appetite and campaigns read, and the <c>risk_to_entity</c> rows the desktop
/// writes. Until the two are unified a scenario is attributed to the deduplicated union of both, so
/// a risk linked to a process only through <c>risk_to_entity</c> still counts in that process, and a
/// risk whose scope entity is also in <c>risk_to_entity</c> counts there once.
///
/// One definition, composable as an <see cref="IQueryable{T}"/> so the risk index can intersect it
/// with its node closures in SQL rather than in memory. It lives outside any risk-index namespace on
/// purpose: attribution is a fact about the register, and a decision service (appetite, campaigns)
/// may read it without depending on the index (S39 §1.3).
///
/// The query runs under the context's entity scope like every other risk query: a scoped caller
/// sees the attributions of the risks it may see — by <c>risks.entity_id</c> — and no others.
/// </summary>
public static class ScenarioAttribution
{
    /// <summary>Every (risk, entity) attribution, each pair once.</summary>
    public static IQueryable<RiskEntityAttribution> Pairs(NRDbContext db)
    {
        var scope = db.Risks
            .Where(r => r.EntityId != null)
            .Select(r => new RiskEntityAttribution { RiskId = r.Id, EntityId = r.EntityId!.Value });

        var linked = db.Risks
            .SelectMany(r => r.Entities, (r, e) => new RiskEntityAttribution { RiskId = r.Id, EntityId = e.Id });

        return scope.Union(linked);
    }

    /// <summary>The ids of the risks attributed to any of <paramref name="entityIds"/>, each once.</summary>
    public static IQueryable<int> RiskIdsAttributedTo(NRDbContext db, IReadOnlyCollection<int> entityIds)
    {
        var ids = entityIds.Distinct().ToArray();

        return Pairs(db)
            .Where(p => ids.Contains(p.EntityId))
            .Select(p => p.RiskId)
            .Distinct();
    }

    /// <summary>
    /// The entities each risk is attributed to, in ascending id order. A risk with no attribution has
    /// no key — the index files it under "Not attributed" (S39 §10.2).
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> EntitiesByRiskAsync(NRDbContext db,
        IReadOnlyCollection<int>? riskIds = null, CancellationToken cancellationToken = default)
    {
        var pairs = Pairs(db);

        if (riskIds != null)
        {
            var ids = riskIds.Distinct().ToArray();
            pairs = pairs.Where(p => ids.Contains(p.RiskId));
        }

        var rows = await pairs.ToListAsync(cancellationToken);

        return rows
            .GroupBy(p => p.RiskId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g.Select(p => p.EntityId).Distinct().OrderBy(id => id).ToList());
    }
}
