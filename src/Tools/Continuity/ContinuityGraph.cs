using Model.Continuity;
using Tools.Risks;

namespace Tools.Continuity;

/// <summary>A business process or IT service as the continuity cascade sees it.</summary>
public sealed record ContinuityNode(
    int EntityId,
    string DefinitionName,
    string? Name,
    bool IsActive,
    int? MtpdMinutes,
    int? RtoMinutes,
    int? RpoMinutes,
    int? EffectiveCriticality = null)
{
    public int? Objective(ContinuityObjective objective) =>
        objective == ContinuityObjective.Rto ? RtoMinutes : RpoMinutes;

    /// <summary>
    /// What this node requires of everything it depends on: its RTO, or its MTPD when it declares no
    /// RTO (RTO ≤ MTPD); for the RPO, its RPO with no fallback. Null imposes nothing — absent is not 0.
    /// </summary>
    public int? Requires(ContinuityObjective objective) =>
        objective == ContinuityObjective.Rto ? RtoMinutes ?? MtpdMinutes : RpoMinutes;

    public bool IsCriticalProcess =>
        DefinitionName == RiskChainSchema.ProcessDefinition
        && EffectiveCriticality is { } c && c >= RiskChainSchema.CriticalThreshold;
}

/// <summary>
/// The continuity dependency graph (Stage 9.3, S43 §4.6, T158): <c>dependent → provider</c> edges
/// between processes and services, and the cascade computed over them.
///
/// <b>Every traversal is an iterative breadth-first walk with a visited set, and a node is never part of
/// its own result</b> — so a cycle (A depends on B depends on A, or a ring of ten thousand) terminates
/// instead of recursing forever (T160). Cycles are not refused: mutual dependency is real, and the
/// recovery deadlock it means is the finding (S43 §11, D7). An edge to an unknown node is dropped.
///
/// The requirement does not add times along the chain (S43 §11, D8): a provider must fit the tightest
/// RTO (or MTPD) of everything that depends on it, transitively.
/// </summary>
public sealed class ContinuityGraph
{
    private static readonly IReadOnlyCollection<int> None = Array.Empty<int>();

    private readonly Dictionary<int, ContinuityNode> _nodes = new();

    /// <summary>dependent → the providers it depends on directly.</summary>
    private readonly Dictionary<int, HashSet<int>> _providers = new();

    /// <summary>provider → the dependents that depend on it directly.</summary>
    private readonly Dictionary<int, HashSet<int>> _dependents = new();

    public ContinuityGraph(IEnumerable<ContinuityNode> nodes, IEnumerable<(int DependentId, int ProviderId)> edges)
    {
        foreach (var node in nodes) _nodes.TryAdd(node.EntityId, node);

        foreach (var (dependent, provider) in edges)
        {
            if (dependent == provider) continue;
            if (!_nodes.ContainsKey(dependent) || !_nodes.ContainsKey(provider)) continue;

            if (!_providers.TryGetValue(dependent, out var providers)) _providers[dependent] = providers = new HashSet<int>();
            providers.Add(provider);

            if (!_dependents.TryGetValue(provider, out var dependents)) _dependents[provider] = dependents = new HashSet<int>();
            dependents.Add(dependent);
        }
    }

    public IReadOnlyDictionary<int, ContinuityNode> Nodes => _nodes;

    public ContinuityNode? Find(int id) => _nodes.GetValueOrDefault(id);

    public IReadOnlyCollection<int> DirectProviders(int id) => _providers.TryGetValue(id, out var p) ? p : None;

    public IReadOnlyCollection<int> DirectDependents(int id) => _dependents.TryGetValue(id, out var d) ? d : None;

    /// <summary>Everything affected if <paramref name="id"/> stops, with the depth of the shortest path.</summary>
    public IReadOnlyDictionary<int, int> Dependents(int id) => Walk(id, DirectDependents);

    /// <summary>Everything <paramref name="id"/> depends on, with the depth of the shortest path.</summary>
    public IReadOnlyDictionary<int, int> Providers(int id) => Walk(id, DirectProviders);

    /// <summary>The other members of <paramref name="id"/>'s strongly connected component.</summary>
    public IReadOnlyCollection<int> CycleMembers(int id)
    {
        var providers = Providers(id);
        return Dependents(id).Keys.Where(providers.ContainsKey).OrderBy(n => n).ToList();
    }

    /// <summary>
    /// The tightest <paramref name="objective"/> the dependents of <paramref name="id"/> require of it,
    /// and the dependent that sets it (smallest value, then shallowest, then lowest id). Null when no
    /// dependent requires anything.
    /// </summary>
    public (int Minutes, int BindingId)? Requirement(int id, ContinuityObjective objective) =>
        Tightest(Dependents(id), node => node.Requires(objective));

    /// <summary>The smallest MTPD among the dependents of <paramref name="id"/>.</summary>
    public (int Minutes, int BindingId)? TightestDependentMtpd(int id) =>
        Tightest(Dependents(id), node => node.MtpdMinutes);

    /// <summary>
    /// The objective <paramref name="id"/> declares is looser than its dependents require: a conflict.
    /// </summary>
    public bool HasConflict(int id, ContinuityObjective objective) =>
        Find(id)?.Objective(objective) is { } declared
        && Requirement(id, objective) is { } required
        && declared > required.Minutes;

    /// <summary>A dependent requires the objective and <paramref name="id"/> does not declare it — not a
    /// conflict (absent is not infinite) and not met (absent is not 0).</summary>
    public bool HasRequirementWithoutObjective(int id, ContinuityObjective objective) =>
        Find(id) is { } node && node.Objective(objective) is null && Requirement(id, objective) is not null;

    /// <summary>The cascade DTO for <paramref name="id"/>.</summary>
    public ContinuityCascadeDto Cascade(int id)
    {
        var dependents = Dependents(id);
        var providers = Providers(id);

        var dto = new ContinuityCascadeDto
        {
            Dependents = ToNodes(dependents),
            Providers = ToNodes(providers),
            CycleMembers = dependents.Keys.Where(providers.ContainsKey).OrderBy(n => n).ToList(),
            RtoRequirement = ToRequirement(Tightest(dependents, n => n.Requires(ContinuityObjective.Rto))),
            RpoRequirement = ToRequirement(Tightest(dependents, n => n.Requires(ContinuityObjective.Rpo))),
            TightestDependentMtpd = ToRequirement(Tightest(dependents, n => n.MtpdMinutes)),
            CriticalDependentCount = dependents.Keys.Count(d => _nodes[d].IsCriticalProcess)
        };

        var node = Find(id);
        if (node is null) return dto;

        foreach (var objective in new[] { ContinuityObjective.Rto, ContinuityObjective.Rpo })
        {
            var required = objective == ContinuityObjective.Rto ? dto.RtoRequirement : dto.RpoRequirement;
            if (required is null) continue;

            if (node.Objective(objective) is { } declared)
            {
                if (declared > required.Minutes)
                    dto.Conflicts.Add(new CascadeConflictDto
                    {
                        Objective = objective, DeclaredMinutes = declared, RequiredMinutes = required.Minutes,
                        BindingEntityId = required.BindingEntityId, BindingName = required.BindingName
                    });
            }
            else
            {
                dto.ObjectivesWithoutValueUnderRequirement.Add(objective);
            }
        }

        return dto;
    }

    private (int Minutes, int BindingId)? Tightest(IReadOnlyDictionary<int, int> candidates,
        Func<ContinuityNode, int?> valueOf)
    {
        (int Minutes, int Depth, int Id)? best = null;

        foreach (var (candidate, depth) in candidates)
        {
            if (valueOf(_nodes[candidate]) is not { } value) continue;

            if (best is null
                || value < best.Value.Minutes
                || (value == best.Value.Minutes && depth < best.Value.Depth)
                || (value == best.Value.Minutes && depth == best.Value.Depth && candidate < best.Value.Id))
                best = (value, depth, candidate);
        }

        return best is null ? null : (best.Value.Minutes, best.Value.Id);
    }

    private CascadeRequirementDto? ToRequirement((int Minutes, int BindingId)? requirement) =>
        requirement is { } r
            ? new CascadeRequirementDto { Minutes = r.Minutes, BindingEntityId = r.BindingId, BindingName = _nodes[r.BindingId].Name }
            : null;

    private List<CascadeNodeDto> ToNodes(IReadOnlyDictionary<int, int> walked) => walked
        .OrderBy(p => p.Value).ThenBy(p => p.Key)
        .Select(p => new CascadeNodeDto
        {
            EntityId = p.Key, Depth = p.Value, Name = _nodes[p.Key].Name, DefinitionName = _nodes[p.Key].DefinitionName
        })
        .ToList();

    private Dictionary<int, int> Walk(int start, Func<int, IReadOnlyCollection<int>> next)
    {
        var depths = new Dictionary<int, int>();
        if (!_nodes.ContainsKey(start)) return depths;

        var visited = new HashSet<int> { start };
        var frontier = new Queue<(int Node, int Depth)>();
        frontier.Enqueue((start, 0));

        while (frontier.Count > 0)
        {
            var (node, depth) = frontier.Dequeue();

            // Ascending ids, so the walk — and any tie it produces — never depends on hash order.
            foreach (var neighbour in next(node).OrderBy(n => n))
            {
                if (!visited.Add(neighbour)) continue;

                depths[neighbour] = depth + 1;
                frontier.Enqueue((neighbour, depth + 1));
            }
        }

        return depths;
    }
}
