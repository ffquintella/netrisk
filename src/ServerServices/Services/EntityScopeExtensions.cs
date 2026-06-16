using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using DAL.Interfaces;

namespace ServerServices.Services;

public static class EntityScopeExtensions
{
    /// <summary>
    /// Filters a queryable data source to only contain records within the user's assigned entity scopes.
    /// Global administrators bypass this check completely.
    /// </summary>
    public static IQueryable<T> ApplyEntityScope<T>(this IQueryable<T> query, ClaimsPrincipal? user, bool bypassScope = false) where T : class, IEntityScoped
    {
        if (bypassScope || user == null)
        {
            return query;
        }

        // Bypassed if user is a global administrator
        if (user.IsInRole("Admin") || user.HasClaim("scope", "global"))
        {
            return query;
        }

        var entityIds = user.Claims
            .Where(c => c.Type == "entity_id")
            .Select(c => int.TryParse(c.Value, out var id) ? (int?)id : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (!entityIds.Any())
        {
            // Deny by default: no assignments means no visibility
            return query.Where(x => false);
        }

        return query.Where(x => x.EntityId.HasValue && entityIds.Contains(x.EntityId.Value));
    }
}
