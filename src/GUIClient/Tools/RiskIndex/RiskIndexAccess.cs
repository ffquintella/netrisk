using System;
using System.Linq;
using Model.Authentication;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// Who may open the Cyber Risk Overview (S40 §10.2, §5.13 "Sem permissão"): the mirror of
/// <c>[PermissionAuthorize(risk_index_view)]</c> on <c>RiskIndexController</c> — the permission itself or the <c>Admin</c>
/// role claim, which <c>PermissionAuthorizationHandler</c> admits for any permission. Enabling only: the API decides.
/// </summary>
public static class RiskIndexAccess
{
    /// <summary>
    /// Whether the navigation button is enabled. A read, so the third line (read-only) is not refused here; anything not
    /// recognised — no user yet, no permission list — is "no".
    /// </summary>
    public static bool CanView(AuthenticatedUserInfo? user) =>
        user is not null &&
        (AdminRoleClaim.IsHeldBy(user) ||
         (user.UserPermissions?.Any(p => string.Equals(p, RiskIndexPermissions.View, StringComparison.Ordinal)) ?? false));
}
