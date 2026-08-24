using System.Collections.Generic;
using System.Linq;

namespace DAL.Context;

/// <summary>
/// The set of business entities the current caller may see (Track 2 milestone 2.3.1/2.3.2).
///
/// This is the value the context's global query filters read. It lives in DAL rather than
/// ServerServices because the filter is declared on the model, and DAL cannot reference the
/// tier above it.
/// </summary>
public sealed class EntityScope
{
    /// <summary>No filtering. Used for global administrators and for non-HTTP callers
    /// (background jobs, the console client) that have no principal to scope by.</summary>
    public static EntityScope Unrestricted { get; } = new(true, []);

    /// <summary>
    /// Deny-by-default: an authenticated user with no entity assignment sees nothing. The 2.3
    /// spec calls for exactly this rather than falling back to "see everything".
    /// </summary>
    public static EntityScope DenyAll { get; } = new(false, []);

    private EntityScope(bool isUnrestricted, IReadOnlyList<int> entityIds)
    {
        IsUnrestricted = isUnrestricted;
        EntityIds = entityIds;
    }

    public static EntityScope ForEntities(IEnumerable<int> entityIds)
    {
        var ids = entityIds.Distinct().ToList();
        return ids.Count == 0 ? DenyAll : new EntityScope(false, ids);
    }

    public bool IsUnrestricted { get; }

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
        return EntityIds.Contains(entityId.Value);
    }

    public override string ToString() =>
        IsUnrestricted ? "unrestricted" : EntityIds.Count == 0 ? "deny-all" : string.Join(",", EntityIds);
}
