using System.Linq;
using System.Threading.Tasks;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Security;

/// <summary>
/// The entity scope a user holds <b>now</b>, read from the database rather than from a request's claims — for work that
/// runs later on that user's behalf (T237 review).
///
/// The job host's principal is unrestricted (T237, S39 D-01). A job that executes what a user configured — a report
/// schedule whose recipients the user chose — must not inherit that: it reads under the scope this returns, so the
/// output never holds more than the user could have read themselves, and a user disabled, locked out or stripped of
/// their entity roles since configuring it gets nothing.
/// </summary>
public interface IUserEntityScopeResolver
{
    /// <summary>
    /// The scope <paramref name="userId"/> would be granted on a request now, or null when the user may not act at all:
    /// unknown, disabled, locked out, or — when <paramref name="requiredPermission"/> is given — no longer holding it.
    /// </summary>
    Task<EntityScope?> ResolveAsync(int userId, string? requiredPermission = null);
}

/// <summary>
/// Mirrors what the API's authentication handlers grant (<c>JwtAuthenticationHandler</c>,
/// <c>BasicAuthenticationHandler</c>): <c>users.admin</c> is the Admin role and <c>scope=global</c>, so unrestricted; any
/// other user gets one claim per unrevoked <c>user_entity_roles</c> row, expanded through the entity tree (T292), and
/// <c>DenyAll</c> with none. Enabled and lockout are checked as the Basic and API-token handlers check them. Permissions
/// are the handlers' own source (<see cref="IPermissionsService"/>), and an administrator passes any, as
/// <c>PermissionAuthorizationHandler</c> lets the Admin role through.
/// </summary>
public sealed class UserEntityScopeResolver(IDalService dalService, IPermissionsService permissionsService)
    : IUserEntityScopeResolver
{
    public async Task<EntityScope?> ResolveAsync(int userId, string? requiredPermission = null)
    {
        await using var db = dalService.GetContext(withIdentity: false, bypassEntityScope: true);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Value == userId);

        if (user is null || user.Enabled != true || user.Lockout != 0) return null;

        if (user.Admin) return EntityScope.Unrestricted;

        if (requiredPermission is not null &&
            !(await permissionsService.GetUserPermissionsAsync(user)).Contains(requiredPermission))
            return null;

        var claimed = await db.UserEntityRoles.AsNoTracking()
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .Select(r => r.EntityId)
            .Distinct()
            .ToListAsync();

        return claimed.Count == 0 ? EntityScope.DenyAll : dalService.GetEntityTree().ScopeFor(claimed);
    }
}
