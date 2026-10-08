using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Model.DecisionCycle;
using Model.Exceptions;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.9 (S50 §4.7) — the service-side half of the third line's read-only rule.
///
/// The API refuses every write of a caller holding <see cref="ThirdLineAssurance.PermissionKey"/> before it reaches a
/// controller (<c>API.Security.ThirdLineReadOnlyRequirement</c>). That covers what the third line <em>does</em>; this
/// covers what others make it do: a manager naming an auditor as the authorizing manager of an acceptance, an
/// administrator appointing one as a business reviewer or a committee member. The decision is the same — the role or the
/// user holds the permission — and the answer is a 422 naming the rule, as segregation of duties answers, administrators
/// included.
/// </summary>
public static class ThirdLineGuard
{
    /// <summary>Whether the user holds the third-line permission, through their role or directly.</summary>
    public static async Task<bool> IsThirdLineAsync(AuditableContext db, int userId)
    {
        ArgumentNullException.ThrowIfNull(db);

        var roleId = await db.Users.AsNoTracking().Where(u => u.Value == userId).Select(u => (int?)u.RoleId)
            .FirstOrDefaultAsync();
        if (roleId is null) return false;

        if (await db.Users.AsNoTracking().Where(u => u.Value == userId)
                .SelectMany(u => u.Permissions).AnyAsync(p => p.Key == ThirdLineAssurance.PermissionKey))
            return true;

        return roleId > 0 && await db.Roles.AsNoTracking().Where(r => r.Value == roleId)
            .SelectMany(r => r.Permissions).AnyAsync(p => p.Key == ThirdLineAssurance.PermissionKey);
    }

    /// <summary>Which of <paramref name="userIds"/> hold the third-line permission — three queries whatever their number.</summary>
    public static async Task<HashSet<int>> ThirdLineAmongAsync(AuditableContext db, IReadOnlyCollection<int> userIds)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userIds.Count == 0) return [];

        var direct = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Value) && u.Permissions.Any(p => p.Key == ThirdLineAssurance.PermissionKey))
            .Select(u => u.Value).ToListAsync();

        var thirdLineRoles = await db.Roles.AsNoTracking()
            .Where(r => r.Permissions.Any(p => p.Key == ThirdLineAssurance.PermissionKey))
            .Select(r => r.Value).ToListAsync();

        var byRole = thirdLineRoles.Count == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Value) && thirdLineRoles.Contains(u.RoleId))
                .Select(u => u.Value).ToListAsync();

        return direct.Concat(byRole).ToHashSet();
    }

    /// <summary>Refuses <paramref name="action"/> when <paramref name="userId"/> is the third line (422).</summary>
    public static async Task EnsureNotThirdLineAsync(AuditableContext db, int userId, string action)
    {
        if (!await IsThirdLineAsync(db, userId)) return;

        Serilog.Log.Warning("Refused '{Action}' for user {User}: the third line reads and never decides", action, userId);

        throw new RuleBrokenException(
            $"User {userId} is in the third line (internal audit), which gives independent assurance and therefore may " +
            $"not {action}. Choose someone in the first or second line.",
            ThirdLineAssurance.CannotApproveRule);
    }
}
