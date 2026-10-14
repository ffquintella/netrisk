using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DAL.Context;

/// <summary>One row of <c>entities</c> as the tree needs it: the id and the parent.</summary>
public readonly record struct EntityTreeRow(int Id, int? Parent);

/// <summary>
/// An immutable snapshot of the entity tree (<c>entities.parent</c>), and the one place that turns a
/// principal's entity claims into a hierarchical <see cref="EntityScope"/> (T292, S39 §10.4, S40 §11.3).
///
/// <para><b>Descendants only.</b> A claim on an entity grants the entity and every descendant reached by
/// walking <em>down</em> the parent links. Nothing walks up, so an ancestor or a sibling is never granted.
/// Links that exist only in EAV properties (a process listing a unit in <c>organizationUnit</c>, an
/// application listed in <c>applications</c>) are not read here at all, so they grant nothing: for the
/// users of a unit to see a process, the process must be the unit's child in the tree.</para>
///
/// <para><b>Malformed nodes.</b> The column has no constraint against cycles. A node whose ancestor chain
/// does not end at a root — it is on a cycle (a self-parent included) or below one — is
/// <see cref="MalformedIds">malformed</see>: it is granted only by an explicit claim on itself, and its
/// children are never expanded. So a cycle neither hangs the traversal (every walk keeps a visited set
/// and is bounded by the node count) nor widens anyone's scope: without it, a claim on any member of a
/// cycle would grant every other member, which is exactly an ancestor grant. A parent id that names no
/// row is treated as a root, as the database's own foreign key would have it.</para>
///
/// <para><b>Cost.</b> Built once per load (O(n)); a scope is computed per distinct claim set in
/// O(size of the granted subtrees) and memoised on the snapshot, so a request pays a dictionary lookup.
/// <see cref="Revision"/> identifies the snapshot, for a cache keyed by the expanded scope.</para>
/// </summary>
public sealed class EntityTree
{
    /// <summary>How many distinct claim sets one snapshot remembers before it stops memoising.</summary>
    private const int MaxMemoisedScopes = 4096;

    private static readonly int[] NoChildren = [];

    private readonly Dictionary<int, int?> _parentById;
    private readonly Dictionary<int, int[]> _childrenById;
    private readonly HashSet<int> _malformed;
    private readonly ConcurrentDictionary<string, EntityScope> _scopes = new(StringComparer.Ordinal);

    private EntityTree(Dictionary<int, int?> parentById, Dictionary<int, int[]> childrenById,
        HashSet<int> malformed, long revision)
    {
        _parentById = parentById;
        _childrenById = childrenById;
        _malformed = malformed;
        Revision = revision;
    }

    /// <summary>An empty tree: every claim grants only itself.</summary>
    public static EntityTree Empty { get; } = Build([], 0);

    /// <summary>Which load of the tree this is. Changes whenever the tree is reloaded.</summary>
    public long Revision { get; }

    /// <summary>How many entities the snapshot holds.</summary>
    public int Count => _parentById.Count;

    /// <summary>The nodes on a parent cycle or below one, which only an explicit claim grants.</summary>
    public IReadOnlyCollection<int> MalformedIds => _malformed;

    public static EntityTree Build(IEnumerable<EntityTreeRow> rows, long revision)
    {
        var parentById = new Dictionary<int, int?>();
        foreach (var row in rows) parentById[row.Id] = row.Parent;

        var children = new Dictionary<int, List<int>>();
        foreach (var (id, parent) in parentById)
        {
            if (parent is not { } p || !parentById.ContainsKey(p)) continue;
            if (!children.TryGetValue(p, out var list)) children[p] = list = [];
            list.Add(id);
        }

        var childrenById = children.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(id => id).ToArray());

        return new EntityTree(parentById, childrenById, FindMalformed(parentById), revision);
    }

    /// <summary>
    /// Marks every node whose upward walk revisits a node. Each node is walked once: a walk stops at the
    /// first node whose answer is already known and hands that answer to the whole path.
    /// </summary>
    private static HashSet<int> FindMalformed(Dictionary<int, int?> parentById)
    {
        // 0 unknown, 1 on the current path, 2 well-formed, 3 malformed.
        var state = new Dictionary<int, byte>(parentById.Count);
        var malformed = new HashSet<int>();
        var path = new List<int>();

        foreach (var start in parentById.Keys)
        {
            if (state.GetValueOrDefault(start) != 0) continue;

            path.Clear();
            int? current = start;
            byte verdict;

            while (true)
            {
                if (current is not { } id || !parentById.TryGetValue(id, out var parent))
                {
                    verdict = 2; // reached a root, or a parent id with no row: well-formed
                    break;
                }

                var seen = state.GetValueOrDefault(id);
                if (seen == 1) { verdict = 3; break; } // back on this walk's own path: a cycle
                if (seen is 2 or 3) { verdict = seen; break; }

                state[id] = 1;
                path.Add(id);
                current = parent;
            }

            foreach (var id in path)
            {
                state[id] = verdict;
                if (verdict == 3) malformed.Add(id);
            }
        }

        return malformed;
    }

    /// <summary>The children of <paramref name="id"/> in the tree, in id order.</summary>
    public IReadOnlyList<int> ChildrenOf(int id) => _childrenById.GetValueOrDefault(id, NoChildren);

    /// <summary>The parent of <paramref name="id"/>, or null for a root or an unknown id.</summary>
    public int? ParentOf(int id) => _parentById.GetValueOrDefault(id);

    public bool Contains(int id) => _parentById.ContainsKey(id);

    /// <summary>
    /// <paramref name="id"/> and every descendant it grants, by a breadth-first walk down the tree. A
    /// malformed node grants itself only. Bounded by the node count.
    /// </summary>
    public IReadOnlySet<int> SelfAndDescendants(int id) => Expand([id]);

    /// <summary>
    /// The hierarchical scope of a principal whose claims name <paramref name="claimedEntityIds"/>:
    /// <see cref="EntityScope.DenyAll"/> for none, otherwise the claims and their descendants.
    /// Memoised per distinct claim set for the life of this snapshot.
    /// </summary>
    public EntityScope ScopeFor(IEnumerable<int> claimedEntityIds)
    {
        var claims = claimedEntityIds.Distinct().OrderBy(id => id).ToArray();
        if (claims.Length == 0) return EntityScope.DenyAll;

        var key = string.Join(",", claims);
        if (_scopes.TryGetValue(key, out var cached)) return cached;

        var granted = Expand(claims);
        var scope = EntityScope.ForClaims(claims, granted.OrderBy(id => id));

        if (_scopes.Count < MaxMemoisedScopes) _scopes.TryAdd(key, scope);
        return scope;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="ancestor"/> or lies below it. Used to refuse
    /// a re-parent that would close a cycle: walks up from the candidate, bounded by the node count.
    /// </summary>
    public bool IsSelfOrDescendant(int candidate, int ancestor)
    {
        var steps = 0;
        int? current = candidate;

        while (current is { } id && steps++ <= _parentById.Count)
        {
            if (id == ancestor) return true;
            if (!_parentById.TryGetValue(id, out current)) return false;
        }

        // Ran out of steps: the candidate sits on or under a cycle. Say yes, so the caller refuses.
        return current is not null;
    }

    private HashSet<int> Expand(IEnumerable<int> roots)
    {
        var granted = new HashSet<int>();
        var queue = new Queue<int>();

        foreach (var root in roots)
        {
            // A claim always grants its own entity, as the flat scope did — even one the snapshot does
            // not hold yet (created after the load) or one that is malformed.
            if (!granted.Add(root)) continue;
            if (!_malformed.Contains(root)) queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            foreach (var child in ChildrenOf(queue.Dequeue()))
            {
                // A well-formed node's children are well-formed; the check is the cycle guard's second
                // line, and the visited set the first.
                if (_malformed.Contains(child) || !granted.Add(child)) continue;
                queue.Enqueue(child);
            }
        }

        return granted;
    }
}
