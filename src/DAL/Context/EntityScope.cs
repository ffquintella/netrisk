using System.Collections.Generic;
using System.Linq;

namespace DAL.Context;

/// <summary>
/// The set of business entities the current caller may see (Track 2 milestone 2.3.1/2.3.2).
///
/// This is the value the context's global query filters read. It lives in DAL rather than
/// ServerServices because the filter is declared on the model, and DAL cannot reference the
/// tier above it.
///
/// Since T292 (S39 §10.4, S40 §11.3) the scope is <b>hierarchical</b>: a claim on an entity grants the
/// entity and every descendant of it in the entity tree (<c>entities.parent</c>), never an ancestor or a
/// sibling. <see cref="ClaimedEntityIds"/> are the entities the principal's claims name;
/// <see cref="EntityIds"/> is those plus their descendants, and it is what every filter and the write
/// guard read. The expansion is done once, by <see cref="EntityTree.ScopeFor"/>, when the scope is built
/// — not in the filter, so it costs nothing per row.
/// </summary>
public sealed class EntityScope
{
    /// <summary>No filtering. Used for global administrators, the Hangfire job host (whose principal carries
    /// <c>scope=global</c>, T237) and callers with no authenticated principal to scope by.</summary>
    public static EntityScope Unrestricted { get; } = new(true, [], []);

    /// <summary>
    /// Deny-by-default: an authenticated user with no entity assignment sees nothing. The 2.3
    /// spec calls for exactly this rather than falling back to "see everything".
    /// </summary>
    public static EntityScope DenyAll { get; } = new(false, [], []);

    private readonly HashSet<int> _allowed;

    private EntityScope(bool isUnrestricted, IReadOnlyList<int> claimedEntityIds, IReadOnlyList<int> entityIds)
    {
        IsUnrestricted = isUnrestricted;
        ClaimedEntityIds = claimedEntityIds;
        EntityIds = entityIds;
        _allowed = [..entityIds];
    }

    /// <summary>
    /// A flat scope: exactly <paramref name="entityIds"/>, each one also counted as claimed. For a caller
    /// that already holds the complete set — a test standing in for a user, an integration test over raw
    /// SQL. A principal's claims go through <see cref="EntityTree.ScopeFor"/> instead, which adds the
    /// descendants.
    /// </summary>
    public static EntityScope ForEntities(IEnumerable<int> entityIds)
    {
        var ids = entityIds.Distinct().ToList();
        return ids.Count == 0 ? DenyAll : new EntityScope(false, ids, ids);
    }

    /// <summary>
    /// A hierarchical scope (T292): <paramref name="claimedEntityIds"/> are what the claims name,
    /// <paramref name="grantedEntityIds"/> what the tree adds to them. No claim is <see cref="DenyAll"/>,
    /// whatever <paramref name="grantedEntityIds"/> holds: descendants are only ever granted through a claim.
    /// </summary>
    public static EntityScope ForClaims(IEnumerable<int> claimedEntityIds, IEnumerable<int> grantedEntityIds)
    {
        var claimed = claimedEntityIds.Distinct().ToList();
        if (claimed.Count == 0) return DenyAll;

        var granted = claimed.Concat(grantedEntityIds).Distinct().ToList();
        return new EntityScope(false, claimed, granted);
    }

    public bool IsUnrestricted { get; }

    /// <summary>The entities the principal's <c>entity_id</c> claims name, without their descendants.</summary>
    public IReadOnlyList<int> ClaimedEntityIds { get; }

    /// <summary>Every entity the caller may see: the claims and, since T292, their descendants.</summary>
    public IReadOnlyList<int> EntityIds { get; }

    /// <summary>
    /// True when this scope allows <paramref name="entityId"/>. Used by the write-side guard,
    /// which the query filter cannot cover: an insert or a re-assignment names an entity the
    /// caller may have no claim to.
    /// </summary>
    public bool Allows(int? entityId)
    {
        if (IsUnrestricted) return true;
        if (entityId == null) return false;
        return _allowed.Contains(entityId.Value);
    }

    /// <summary>
    /// The claims, and how many descendants they bring — not the whole expanded list, which can be long
    /// and ends up in exception messages and logs.
    /// </summary>
    public override string ToString()
    {
        if (IsUnrestricted) return "unrestricted";
        if (EntityIds.Count == 0) return "deny-all";

        var claims = string.Join(",", ClaimedEntityIds);
        var descendants = EntityIds.Count - ClaimedEntityIds.Count;
        return descendants == 0 ? claims : $"{claims} (+{descendants} descendants)";
    }
}
