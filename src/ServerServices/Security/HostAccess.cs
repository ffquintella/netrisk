using System.Security.Claims;

namespace ServerServices.Security;

/// <summary>
/// Whether a principal may read hosts — the rule <c>[PermissionAuthorize("hosts")]</c> applies to the
/// whole <c>HostsController</c>, restated for a service that reaches hosts by another route.
///
/// The linkage chain (Stage 9.1, S41 §6) can point a risk at a host, and its routes sit under
/// <c>riskmanagement</c>, not <c>hosts</c>. Without this check the chain would be a side door to the
/// host inventory for anyone who manages risks. The rule matches <c>PermissionAuthorizationHandler</c>
/// exactly — the <c>Admin</c> role or the <c>hosts</c> permission claim — so the two audiences cannot
/// drift apart.
///
/// A null principal is a denial, never "unrestricted": a job or the console that calls the chain
/// service without a user must not see host details by accident (S41 §11, R9).
/// </summary>
public static class HostAccess
{
    public const string Permission = "hosts";

    public static bool CanRead(ClaimsPrincipal? user)
    {
        if (user is null) return false;

        return user.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin")
               || user.HasClaim(c => c.Type == "Permission" && c.Value == Permission);
    }
}
