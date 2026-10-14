using Model.Authentication;
using Model.Entities;

namespace GUIClient.Tools;

/// <summary>
/// Who may edit the entity map from the desktop Entities screen — the client-side mirror of
/// <c>EntitiesController</c> (T241), used only to enable controls. The API decides every call: <c>POST</c>,
/// <c>PUT</c> and <c>DELETE /Entities</c> carry <c>[PermissionAuthorize("entities_manage")]</c>, and
/// <c>EntitiesService.AuthorizeEntityWrite</c> refuses a risk-context change without <c>entity_risk_context</c> or
/// without global scope, and any write outside the caller's subtree. The mirror cannot see the caller's scope, so it
/// gates on the permissions alone; it fails closed and never admits the third line (<see cref="PermissionGate"/>).
/// </summary>
public static class EntityAccess
{
    /// <summary>Add, edit, delete and save an entity: <c>entities_manage</c> or the Admin role claim.</summary>
    public static bool CanManage(AuthenticatedUserInfo? user) => PermissionGate.Allows(user, EntityPermissions.Manage);

    /// <summary>Change criticality, internet exposure or security classification: <c>entity_risk_context</c>.</summary>
    public static bool CanEditRiskContext(AuthenticatedUserInfo? user) =>
        PermissionGate.Allows(user, EntityPermissions.RiskContext);

    /// <summary>
    /// Whether the form field for <paramref name="propertyKey"/> is read-only for <paramref name="user"/>: every
    /// field without <c>entities_manage</c>, and a <see cref="RiskContextProperties">risk-context field</see>
    /// without <c>entity_risk_context</c>.
    /// </summary>
    public static bool IsFieldReadOnly(string propertyKey, AuthenticatedUserInfo? user) =>
        !CanManage(user) || (RiskContextProperties.IsRiskContext(propertyKey) && !CanEditRiskContext(user));

    /// <summary>
    /// True when the field is read-only <em>because</em> it is risk context — the case the form explains under the
    /// field. A user without <c>entities_manage</c> gets one notice for the whole form instead.
    /// </summary>
    public static bool IsRiskContextLocked(string propertyKey, AuthenticatedUserInfo? user) =>
        CanManage(user) && RiskContextProperties.IsRiskContext(propertyKey) && !CanEditRiskContext(user);
}
