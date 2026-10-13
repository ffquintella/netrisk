using Model.Authentication;

namespace GUIClient.Tools;

/// <summary>
/// Who may write the risk record from the desktop risk screen — the client-side mirror of the policies on
/// <c>RisksController</c>, used only to enable buttons (T328). The API decides every call; what the mirror must not
/// do is say "yes" where the server says "no", so it fails closed, and never admits the third line, which
/// <c>ThirdLineReadOnlyRequirement</c> refuses every write (<see cref="PermissionGate"/> carries that rule).
///
/// Before this the bar under the risk list enabled Add and Edit for everyone, and gated Close — and Reopen, which
/// deletes the closure — on <c>delete_risk</c>, which is not the permission the close endpoints check.
///
/// Free of Avalonia so <c>GUIClient.Tests</c> covers it.
/// </summary>
public static class RiskRecordAccess
{
    public const string SubmitPermission = "submit_risks";
    public const string ClosePermission = "close_risks";
    public const string DeletePermission = "delete_risk";

    /// <summary>
    /// Add and Edit: <c>POST /Risks</c>, <c>PUT /Risks/{id}</c> and their scoring calls carry
    /// <c>RequireSubmitRisk</c>, a claims requirement on the <c>submit_risks</c> permission alone — the Admin role is
    /// not part of it, so an administrator without the permission is refused too.
    /// </summary>
    public static bool CanSubmit(AuthenticatedUserInfo? user) =>
        PermissionGate.Allows(user?.UserPermissions, SubmitPermission);

    /// <summary>
    /// Close and Reopen: <c>POST</c> and <c>DELETE /Risks/{id}/Closure</c> carry <c>RequireCloseRisk</c> — the Admin
    /// role claim or <c>close_risks</c>.
    /// </summary>
    public static bool CanClose(AuthenticatedUserInfo? user) => PermissionGate.Allows(user, ClosePermission);

    /// <summary>Delete: <c>DELETE /Risks/{id}</c> carries <c>RequireDeleteRisk</c> — the Admin role claim or <c>delete_risk</c>.</summary>
    public static bool CanDelete(AuthenticatedUserInfo? user) => PermissionGate.Allows(user, DeletePermission);
}
