using System.Security.Claims;

namespace ServerServices.Security;

/// <summary>
/// Who may write Stage 9.3 continuity data (S43 §6, D11), evaluated from a principal the same way
/// <c>PermissionAuthorizationHandler</c> evaluates <c>[PermissionAuthorize]</c>: the <c>Admin</c> role
/// or the permission claim.
///
/// The API enforces the permissions with the attributes; this helper exists so the profile can tell the
/// client which buttons to enable (<c>CallerCanManageBia</c>, <c>CallerCanRecordTests</c>) with the very
/// rule the server applies. A null principal — a job, the console — holds nothing.
/// </summary>
public static class ContinuityAccess
{
    public const string ManageBiaPermission = "bia_manage";
    public const string RecordTestsPermission = "restoration_test_record";

    /// <summary>The permission name a scoped caller is refused under: writes need global scope.</summary>
    public const string GlobalScope = "global_scope";

    public static bool CanManageBia(ClaimsPrincipal? user) => Has(user, ManageBiaPermission);

    public static bool CanRecordTests(ClaimsPrincipal? user) => Has(user, RecordTestsPermission);

    private static bool Has(ClaimsPrincipal? user, string permission) =>
        user is not null
        && (user.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin")
            || user.HasClaim(c => c.Type == "Permission" && c.Value == permission));
}
