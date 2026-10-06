using System.Globalization;

namespace Tools.Risks;

/// <summary>
/// One entity as the chain sees it: its id, definition, parent and the property rows the chain reads.
/// Multi-valued properties are stored as one <c>entities_properties</c> row per value, so a property
/// maps to a list.
/// </summary>
public sealed class RiskChainNode
{
    private static readonly IReadOnlyList<string> NoValues = Array.Empty<string>();

    private readonly Dictionary<string, List<string>> _properties = new(StringComparer.Ordinal);

    public RiskChainNode(int id, string definitionName, int? parentId = null,
        IEnumerable<KeyValuePair<string, string>>? properties = null)
    {
        Id = id;
        DefinitionName = definitionName;
        ParentId = parentId;

        if (properties is null) return;

        foreach (var (type, value) in properties)
        {
            if (!_properties.TryGetValue(type, out var values))
                _properties[type] = values = new List<string>();
            values.Add(value);
        }
    }

    public int Id { get; }

    public string DefinitionName { get; }

    public int? ParentId { get; }

    public IReadOnlyList<string> Values(string property) =>
        _properties.TryGetValue(property, out var values) ? values : NoValues;

    public string? Value(string property) =>
        _properties.TryGetValue(property, out var values) && values.Count > 0 ? values[0] : null;

    public string? Name => Value(RiskChainSchema.NameProperty);
}

/// <summary>
/// The typed "serves" graph between chain nodes (Stage 9.1, S41 §4.2), built once from the entities
/// and their properties and then queried in memory.
///
/// Edges point from a lower node to the upper node it serves: application → service → process →
/// objective, data → service, application → process, module → application, activity → process. An
/// edge is built only when its referenced id exists <b>and</b> is of the declared type; anything else —
/// a dangling id left by a deleted entity, a <c>processes</c> value that names an application, a value
/// that is not a number — is dropped without an exception. Saving an entity does not check its
/// references (S41 §2), so the resolver has to tolerate whatever is stored.
///
/// Inference only goes up. <see cref="Below"/> returns the nodes whose risks count towards a node; a
/// risk of a process never shows up under the service that serves it.
/// </summary>
public sealed class RiskChainGraph
{
    private static readonly IReadOnlyCollection<int> None = Array.Empty<int>();

    private readonly Dictionary<int, RiskChainNode> _nodes = new();

    /// <summary>lower → the upper nodes it serves.</summary>
    private readonly Dictionary<int, HashSet<int>> _up = new();

    /// <summary>upper → the lower nodes that serve it.</summary>
    private readonly Dictionary<int, HashSet<int>> _down = new();

    public RiskChainGraph(IEnumerable<RiskChainNode> nodes)
    {
        foreach (var node in nodes) _nodes.TryAdd(node.Id, node);

        foreach (var owner in _nodes.Values)
        {
            foreach (var edge in RiskChainSchema.Edges)
            {
                if (!string.Equals(owner.DefinitionName, edge.OwnerDefinition, StringComparison.Ordinal))
                    continue;

                foreach (var referencedId in ReferencedIds(owner, edge))
                {
                    if (referencedId == owner.Id) continue;
                    if (!_nodes.TryGetValue(referencedId, out var referenced)) continue;
                    if (!string.Equals(referenced.DefinitionName, edge.ReferencedDefinition, StringComparison.Ordinal))
                        continue;

                    if (edge.OwnerIsLower) Connect(owner.Id, referencedId);
                    else Connect(referencedId, owner.Id);
                }
            }
        }
    }

    public IReadOnlyDictionary<int, RiskChainNode> Nodes => _nodes;

    public RiskChainNode? Find(int id) => _nodes.GetValueOrDefault(id);

    /// <summary>The nodes <paramref name="id"/> directly serves.</summary>
    public IReadOnlyCollection<int> DirectlyAbove(int id) => _up.TryGetValue(id, out var up) ? up : None;

    /// <summary>The nodes that directly serve <paramref name="id"/>.</summary>
    public IReadOnlyCollection<int> DirectlyBelow(int id) => _down.TryGetValue(id, out var down) ? down : None;

    /// <summary>
    /// Every node below <paramref name="id"/> — every node whose risks are inferred onto it — with the
    /// length of the shortest path to it. The node itself is never included, even when a cycle leads
    /// back to it, and a cycle cannot make the walk loop: each node is visited once.
    /// </summary>
    public IReadOnlyDictionary<int, int> Below(int id)
    {
        var depths = new Dictionary<int, int>();
        if (!_nodes.ContainsKey(id)) return depths;

        var visited = new HashSet<int> { id };
        var frontier = new Queue<(int Node, int Depth)>();
        frontier.Enqueue((id, 0));

        while (frontier.Count > 0)
        {
            var (node, depth) = frontier.Dequeue();

            // Ascending ids so the walk — and so any tie it produces — does not depend on hash order.
            foreach (var lower in DirectlyBelow(node).OrderBy(n => n))
            {
                if (!visited.Add(lower)) continue;

                depths[lower] = depth + 1;
                frontier.Enqueue((lower, depth + 1));
            }
        }

        return depths;
    }

    /// <summary>
    /// Of the nodes a risk is linked to, the one an inferred match is reported "via": the shallowest
    /// below the queried node, then the lowest id. Null when none of them is below it.
    /// </summary>
    public static (int NodeId, int Depth)? PickVia(IEnumerable<int> linkedNodeIds, IReadOnlyDictionary<int, int> below)
    {
        (int NodeId, int Depth)? best = null;

        foreach (var nodeId in linkedNodeIds)
        {
            if (!below.TryGetValue(nodeId, out var depth)) continue;

            if (best is null || depth < best.Value.Depth || (depth == best.Value.Depth && nodeId < best.Value.NodeId))
                best = (nodeId, depth);
        }

        return best;
    }

    private void Connect(int lower, int upper)
    {
        if (lower == upper) return;

        if (!_up.TryGetValue(lower, out var up)) _up[lower] = up = new HashSet<int>();
        up.Add(upper);

        if (!_down.TryGetValue(upper, out var down)) _down[upper] = down = new HashSet<int>();
        down.Add(lower);
    }

    private static IEnumerable<int> ReferencedIds(RiskChainNode owner, RiskChainEdge edge)
    {
        if (edge.IsParentEdge)
        {
            if (owner.ParentId is { } parent) yield return parent;
            yield break;
        }

        foreach (var raw in owner.Values(edge.Property!))
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                yield return id;
        }
    }
}
