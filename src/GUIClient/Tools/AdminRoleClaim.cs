using System;
using Model.Authentication;

namespace GUIClient.Tools;

/// <summary>
/// The desktop's mirror of the server's <c>Admin</c> role claim — for showing and enabling only; the API decides
/// every call.
///
/// <c>JwtAuthenticationHandler</c> and <c>BasicAuthenticationHandler</c> add <c>ClaimTypes.Role = "Admin"</c> when the
/// account's admin flag is set, next to a role claim carrying the role's own name (so a role literally named
/// "Admin" yields the same claim). <see cref="AuthenticatedUserInfo"/> carries the flag as <c>IsAdmin</c> and only the
/// role's name as <c>UserRole</c>, so comparing <c>UserRole</c> with "Admin" misses an administrator whose role is
/// named anything else — the screen locks while the API would accept the write.
///
/// Neither method knows about the third line: <c>ThirdLineReadOnlyRequirement</c> refuses every write to a holder of
/// <c>third_line_assurance</c>, administrators included, so callers keep checking it first.
/// </summary>
public static class AdminRoleClaim
{
    /// <summary>
    /// Whether the server issues the <c>Admin</c> role claim to <paramref name="user"/>: what
    /// <c>PermissionAuthorizationHandler</c> admits for any <c>[PermissionAuthorize]</c>, and what
    /// <c>RequireCloseRisk</c>, <c>RequireDeleteRisk</c> and the Track 9 register read policies accept.
    /// Not the audience of <c>RequireRiskmanagement</c>, which accepts only the <c>Administrator</c> role.
    /// </summary>
    public static bool IsHeldBy(AuthenticatedUserInfo? user) =>
        user is not null && (user.IsAdmin || string.Equals(user.UserRole, "Admin", StringComparison.Ordinal));

    /// <summary>The audience of <c>RequireAdminOnly</c>: the <c>Admin</c> role claim or the <c>Administrator</c> role.</summary>
    public static bool InAdminOnlyAudience(AuthenticatedUserInfo? user) =>
        IsHeldBy(user) || string.Equals(user?.UserRole, "Administrator", StringComparison.Ordinal);
}
