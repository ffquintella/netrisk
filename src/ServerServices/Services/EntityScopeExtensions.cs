using System;
using System.Linq;
using System.Security.Claims;
using DAL.Context;
using DAL.Interfaces;
using ServerServices.Security;

namespace ServerServices.Services;

public static class EntityScopeExtensions
{
    /// <summary>
    /// The scope of an explicitly given principal — the same rule as <c>DalService.GetCurrentEntityScope</c>
    /// (<see cref="EntityScopeClaims"/>), expanded through the same tree (T292). <paramref name="tree"/> is only
    /// called when there is a claim to expand.
    /// </summary>
    public static EntityScope ScopeOf(ClaimsPrincipal user, Func<EntityTree> tree)
    {
        if (EntityScopeClaims.IsUnrestricted(user)) return EntityScope.Unrestricted;

        var claimed = EntityScopeClaims.ClaimedEntityIds(user);
        return claimed.Count == 0 ? EntityScope.DenyAll : tree().ScopeFor(claimed);
    }

    /// <summary>
    /// Filters a queryable data source to the records <paramref name="scope"/> allows. The context's own query
    /// filter already applies the caller's scope; this narrows further when a query must answer for another
    /// principal. Unrestricted passes everything; an empty scope passes nothing.
    /// </summary>
    public static IQueryable<T> ApplyEntityScope<T>(this IQueryable<T> query, EntityScope scope) where T : class, IEntityScoped
    {
        if (scope.IsUnrestricted) return query;

        // Deny by default: no assignments means no visibility
        if (scope.EntityIds.Count == 0) return query.Where(x => false);

        var entityIds = scope.EntityIds.ToArray();
        return query.Where(x => x.EntityId.HasValue && entityIds.Contains(x.EntityId.Value));
    }
}
