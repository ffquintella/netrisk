using Model.Authentication;
namespace GUIClient.Tools.Track9;
public static class Track9WorkspaceAccess
{
    // These are the exact policy audiences. IsAdmin alone is not RequireRiskmanagement.
    public static bool CanReadRisk(AuthenticatedUserInfo? user) => user is not null &&
        (user.UserRole == "Administrator" || user.UserPermissions?.Contains("riskmanagement") == true);
    public static bool CanReadRegister(AuthenticatedUserInfo? user, string permission) => user is not null &&
        (CanReadRisk(user) || user.UserRole == "Admin" || user.UserPermissions?.Contains(permission) == true);
    public static bool CanOpen(AuthenticatedUserInfo? user) =>
        CanReadRegister(user, "third_party_manage") || CanReadRegister(user, "data_catalogue_manage") ||
        CanReadRegister(user, "ai_governance_manage");
}
