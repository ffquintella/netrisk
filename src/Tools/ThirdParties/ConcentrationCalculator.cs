using DAL.Enums;
using Model.Continuity;
using Model.ThirdParties;
using Tools.Continuity;
using Tools.Risks;

namespace Tools.ThirdParties;

/// <summary>A third party as the concentration sees it.</summary>
/// <param name="Id">The third party.</param>
/// <param name="Name">Its name.</param>
/// <param name="Status">Only <see cref="ThirdPartyStatus.Active"/> and <see cref="ThirdPartyStatus.Exiting"/> supply.</param>
/// <param name="IsCloudProvider">Counted in the cloud dimension.</param>
/// <param name="IsIdentityProvider">Counted in the identity dimension.</param>
/// <param name="SuppliedEntityIds">The IT services and business processes it is linked to (data links excluded).</param>
public sealed record SupplierFacts(
    int Id,
    string Name,
    ThirdPartyStatus Status,
    bool IsCloudProvider,
    bool IsIdentityProvider,
    IReadOnlyCollection<int> SuppliedEntityIds)
{
    /// <summary>Whether the relationship with the organization is live (S51 D6).</summary>
    public bool Supplies => Status is ThirdPartyStatus.Active or ThirdPartyStatus.Exiting;
}

/// <summary>What one third party carries (S51 §4.8): the critical processes that depend on it, and what they require of it.</summary>
public sealed class SupplierExposure
{
    /// <summary>Distinct active critical processes — each once, however many links, services or paths lead to it.</summary>
    public IReadOnlyCollection<int> CriticalProcessIds { get; init; } = Array.Empty<int>();

    /// <summary>Some of them are reached only because a supplying third party names this one as a sub-processor.</summary>
    public bool ReachedThroughSubprocessing { get; init; }

    /// <summary>Its own supplied services with no dependent declared in the BIA.</summary>
    public int LinksWithoutDeclaredDependents { get; init; }

    /// <summary>
    /// Supplied directly while live, or reached as a sub-processor of a supplier that is live — what makes it appear in
    /// the concentration at all.
    /// </summary>
    public bool InUse { get; init; }

    public ThirdPartyContinuityRequirementDto Requirement { get; init; } = new();
}

/// <summary>
/// Concentration by supplier, cloud and identity (Stage 9.10, S51 §4.8, T203, T205), computed on read and never stored.
///
/// <b>A supplier is counted once per dependent critical process, never once per link or per asset</b> — S27's edge case
/// for this stage: "otherwise the metric measures inventory, not concentration". So the measure is the size of a
/// <em>set</em> of process ids: a supplier linked to three services that all serve one critical process carries one.
///
/// What depends on a third party T:
/// <list type="number">
/// <item>the services and processes T supplies (its <c>third_party_links</c> of kind IT service or process) — while its
/// relationship with the organization is live (active or exiting);</item>
/// <item>everything that depends on those in the Stage 9.3 BIA cascade (<see cref="ContinuityGraph.Dependents"/>, the
/// declared and audited <c>bia_dependencies</c> — never the unaudited "serves" properties of the chain, S43 D6);</item>
/// <item>and, when a live supplier A names T as a registered sub-processor, everything that depends on A — transitively
/// and cycle-safe: T failing takes A down. This is the fourth-party path that makes cloud concentration real (a SaaS
/// hosted on the same cloud as the organization's own workloads).</item>
/// </list>
/// Of that, the processes counted are the active ones whose effective criticality (BIA-derived first, S43 D4) is critical.
/// </summary>
public static class ConcentrationCalculator
{
    public static Dictionary<int, SupplierExposure> Compute(ContinuityGraph graph, IReadOnlyCollection<SupplierFacts> suppliers,
        IReadOnlyCollection<(int UserId, int SubprocessorId)> subprocessing)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(subprocessing);

        var byId = suppliers.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());

        // sub-processor → the suppliers that name it.
        var usersOf = new Dictionary<int, HashSet<int>>();
        foreach (var (user, sub) in subprocessing)
        {
            if (user == sub || !byId.ContainsKey(user) || !byId.ContainsKey(sub)) continue;
            if (!usersOf.TryGetValue(sub, out var users)) usersOf[sub] = users = new HashSet<int>();
            users.Add(user);
        }

        var result = new Dictionary<int, SupplierExposure>();
        foreach (var supplier in byId.Values)
        {
            // T and everything that uses it, directly or through other sub-processors — whatever their status: A's
            // dependency on its sub-processor B is A's, not the organization's relationship with B.
            var users = Walk(supplier.Id, usersOf);

            var ownRoots = supplier.Supplies ? supplier.SuppliedEntityIds.ToHashSet() : new HashSet<int>();
            var viaRoots = users
                .Where(u => byId[u].Supplies)
                .SelectMany(u => byId[u].SuppliedEntityIds)
                .ToHashSet();

            var own = CriticalDependents(graph, ownRoots);
            var all = new HashSet<int>(own);
            all.UnionWith(CriticalDependents(graph, viaRoots));

            // What it must contract for is read from what it is linked to whatever the stage of the relationship — a
            // prospective supplier is exactly when the RTO is negotiated — and only a terminated one is exempt.
            var requirementRoots = supplier.Status == ThirdPartyStatus.Terminated
                ? new HashSet<int>()
                : supplier.SuppliedEntityIds.ToHashSet();
            requirementRoots.UnionWith(viaRoots);

            result[supplier.Id] = new SupplierExposure
            {
                CriticalProcessIds = all.OrderBy(id => id).ToList(),
                ReachedThroughSubprocessing = all.Count > own.Count,
                LinksWithoutDeclaredDependents = ownRoots.Count(r =>
                    graph.Find(r) is { } node && node.DefinitionName == RiskChainSchema.ItServiceDefinition
                                              && graph.DirectDependents(r).Count == 0),
                InUse = supplier.Supplies || users.Any(u => byId[u].Supplies),
                Requirement = RequirementOf(graph, requirementRoots)
            };
        }

        return result;
    }

    /// <summary>The organization's active critical processes — the denominator of every share.</summary>
    public static int CriticalProcessCount(ContinuityGraph graph) =>
        graph.Nodes.Values.Count(n => n.IsActive && n.IsCriticalProcess);

    /// <summary>The share of <paramref name="count"/> over <paramref name="total"/>, four places; null when there is no critical process.</summary>
    public static decimal? Share(int count, int total) =>
        total == 0 ? null : System.Math.Round((decimal)count / total, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The tightest RTO (or MTPD) and RPO the supplied services and processes require — their own objectives and those of
    /// everything that depends on them (S51 §4.7). The binding entity is the one that sets it (smallest, then lowest id).
    /// Absent stays absent: a supplier of something with no objective has no requirement, not a requirement of 0.
    /// </summary>
    public static ThirdPartyContinuityRequirementDto RequirementOf(ContinuityGraph graph, IReadOnlyCollection<int> roots)
    {
        var dto = new ThirdPartyContinuityRequirementDto();

        foreach (var objective in new[] { ContinuityObjective.Rto, ContinuityObjective.Rpo })
        {
            (int Minutes, int Binding)? best = null;

            foreach (var root in roots.OrderBy(r => r))
            {
                if (graph.Find(root) is not { } node) continue;

                Consider(node.Requires(objective), root);
                if (graph.Requirement(root, objective) is { } required) Consider(required.Minutes, required.BindingId);
            }

            if (best is not { } found) continue;

            var name = graph.Find(found.Binding)?.Name;
            if (objective == ContinuityObjective.Rto)
                (dto.RequiredRtoMinutes, dto.RtoBindingEntityId, dto.RtoBindingName) = (found.Minutes, found.Binding, name);
            else
                (dto.RequiredRpoMinutes, dto.RpoBindingEntityId, dto.RpoBindingName) = (found.Minutes, found.Binding, name);

            void Consider(int? minutes, int binding)
            {
                if (minutes is not { } m) return;
                if (best is null || m < best.Value.Minutes || (m == best.Value.Minutes && binding < best.Value.Binding))
                    best = (m, binding);
            }
        }

        return dto;
    }

    private static HashSet<int> CriticalDependents(ContinuityGraph graph, IEnumerable<int> roots)
    {
        var processes = new HashSet<int>();
        foreach (var root in roots)
        {
            if (graph.Find(root) is not { } node) continue;

            if (node.IsActive && node.IsCriticalProcess) processes.Add(root);

            foreach (var dependent in graph.Dependents(root).Keys)
                if (graph.Find(dependent) is { IsActive: true, IsCriticalProcess: true })
                    processes.Add(dependent);
        }

        return processes;
    }

    /// <summary>Everything that uses <paramref name="start"/> as a sub-processor, transitively; a visited set ends cycles.</summary>
    private static HashSet<int> Walk(int start, IReadOnlyDictionary<int, HashSet<int>> usersOf)
    {
        var visited = new HashSet<int> { start };
        var frontier = new Queue<int>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
            if (usersOf.TryGetValue(frontier.Dequeue(), out var users))
                foreach (var user in users.OrderBy(u => u))
                    if (visited.Add(user))
                        frontier.Enqueue(user);

        visited.Remove(start);
        return visited;
    }
}
