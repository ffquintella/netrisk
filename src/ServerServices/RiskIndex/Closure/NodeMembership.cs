using DAL.Enums;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Closure;

/// <summary>
/// Which entities an object is attributed to (S39 §10.2) and the scope entity of its source row (S40 §7.4) — the
/// column a reader's claims are compared with. No attribution files the object under "Unassigned".
/// </summary>
public sealed record ObjectAttribution(IcrObjectKey Key, IReadOnlyCollection<int> Entities, int? ScopeEntityId);

/// <summary>A node the index scores (S39 §2.2, §9.1).</summary>
public sealed record RiskIndexNodeDefinition
{
    public required RiskIndexScopeKind ScopeKind { get; init; }

    /// <summary><c>scope_ref</c>: "org", the entity id, "u" for Unassigned, the asset class or the source id.</summary>
    public required string ScopeRef { get; init; }

    /// <summary>The node's entity — the snapshot row's scope filter (S40 §7.4); null for the root, Unassigned, classes and sources.</summary>
    public int? EntityId { get; init; }

    public string? EntityType { get; init; }

    public bool Unassigned { get; init; }

    public RiskIndexAssetClass? AssetClass { get; init; }

    public string? Source { get; init; }
}

/// <summary>
/// The members of one node on one day (S39 §10.1; S40 §7.2 <c>risk_index_node_members</c>): the closure entities, the
/// deduplicated objects, and the scope entities of those objects — what an official read compares with the claims.
/// </summary>
public sealed record NodeMembers
{
    public required RiskIndexNodeDefinition Node { get; init; }
    public IReadOnlySet<int> Closure { get; init; } = new HashSet<int>();
    public IReadOnlySet<IcrObjectKey> Objects { get; init; } = new HashSet<IcrObjectKey>();
    public IReadOnlySet<int> ScopeEntities { get; init; } = new HashSet<int>();

    /// <summary>Some member object has no scope entity: only an unrestricted reader sees the whole node.</summary>
    public bool HasUnscopedObjects { get; init; }

    /// <summary>The closure reaches entities through EAV links only — never an access grant (T292), so a scoped reader
    /// gets a partial live read there (S39 §10.4).</summary>
    public bool ClosureLinkedOnlyThroughEav { get; init; }
}

/// <summary>
/// Set-based node membership (S39 §10.1–§10.3): a node holds the deduplicated union of the objects attributed to any
/// entity of its closure — never a roll-up of its children's indices. An object shared by two processes belongs wholly
/// to each, and once to any node containing both.
/// </summary>
public static class NodeMembership
{
    public const string RootRef = "org";
    public const string UnassignedRef = "u";

    public static readonly RiskIndexNodeDefinition Root = new() { ScopeKind = RiskIndexScopeKind.Organization, ScopeRef = RootRef };

    public static readonly RiskIndexNodeDefinition UnassignedNode = new()
    {
        ScopeKind = RiskIndexScopeKind.Entity, ScopeRef = UnassignedRef, Unassigned = true
    };

    /// <summary>
    /// The root, every entity of <paramref name="nodeTypes"/> (the pre-computed types by default) and the Unassigned
    /// node. Attribution to an entity the map does not hold counts as none.
    /// </summary>
    public static IReadOnlyList<NodeMembers> Resolve(EntityMap map, IReadOnlyCollection<ObjectAttribution> objects,
        IReadOnlySet<string>? nodeTypes = null)
    {
        var types = nodeTypes ?? RiskIndexEntityTypes.PrecomputedNodeTypes;
        var byEntity = new Dictionary<int, List<ObjectAttribution>>();
        var unassigned = new List<ObjectAttribution>();

        foreach (var o in objects)
        {
            var known = o.Entities.Where(map.Contains).Distinct().ToList();
            if (known.Count == 0)
            {
                unassigned.Add(o);
                continue;
            }

            foreach (var entity in known)
            {
                if (!byEntity.TryGetValue(entity, out var list)) byEntity[entity] = list = [];
                list.Add(o);
            }
        }

        var result = new List<NodeMembers> { Members(Root, map.Ids.ToHashSet(), objects, false) };

        foreach (var id in map.Ids.Where(id => map.DefinitionOf(id) is { } d && types.Contains(d)).OrderBy(id => id))
        {
            var closure = map.Closure(id);
            var members = closure.Members.SelectMany(m => byEntity.GetValueOrDefault(m) ?? []);
            result.Add(Members(new RiskIndexNodeDefinition
            {
                ScopeKind = RiskIndexScopeKind.Entity, ScopeRef = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                EntityId = id, EntityType = map.DefinitionOf(id)
            }, closure.Members, members, closure.LinkedOnlyThroughEav));
        }

        result.Add(Members(UnassignedNode, new HashSet<int>(), unassigned, false));
        return result;
    }

    /// <summary>The objects of a node that are attributed to <paramref name="entities"/> — a derived node's set (S39 §9.1).</summary>
    public static IReadOnlySet<IcrObjectKey> ObjectsOf(IEnumerable<ObjectAttribution> objects, IReadOnlySet<int> entities) =>
        objects.Where(o => o.Entities.Any(entities.Contains)).Select(o => o.Key).ToHashSet();

    private static NodeMembers Members(RiskIndexNodeDefinition node, IReadOnlySet<int> closure,
        IEnumerable<ObjectAttribution> objects, bool eavOnly)
    {
        var list = objects.DistinctBy(o => o.Key).ToList();
        return new NodeMembers
        {
            Node = node,
            Closure = closure,
            Objects = list.Select(o => o.Key).ToHashSet(),
            ScopeEntities = list.Where(o => o.ScopeEntityId is not null).Select(o => o.ScopeEntityId!.Value).ToHashSet(),
            HasUnscopedObjects = list.Any(o => o.ScopeEntityId is null),
            ClosureLinkedOnlyThroughEav = eavOnly
        };
    }
}
