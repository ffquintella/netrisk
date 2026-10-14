using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ServerServices.Security;

/// <summary>
/// How a principal's claims state its entity scope — read in one place, so the context's scope
/// (<c>DalService.GetCurrentEntityScope</c>) and the explicit-principal filter
/// (<c>EntityScopeExtensions.ApplyEntityScope</c>) cannot drift apart.
/// </summary>
public static class EntityScopeClaims
{
    /// <summary>One claim per entity the principal is assigned to (the JWT, Basic and API-token handlers).</summary>
    public const string EntityIdClaim = "entity_id";

    /// <summary>
    /// Unrestricted: the <c>Admin</c> role, or <c>scope=global</c> — which the handlers issue to an
    /// administrator only and the job host's principal carries (T237). Global still means every entity.
    /// </summary>
    public static bool IsUnrestricted(ClaimsPrincipal user) =>
        user.HasClaim("scope", "global") || user.IsInRole("Admin");

    /// <summary>The entities the claims name, without their descendants. Unparseable values are ignored.</summary>
    public static IReadOnlyList<int> ClaimedEntityIds(ClaimsPrincipal user) =>
        user.Claims
            .Where(c => c.Type == EntityIdClaim)
            .Select(c => int.TryParse(c.Value, out var id) ? (int?)id : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
}
