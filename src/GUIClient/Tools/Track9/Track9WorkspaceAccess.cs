using Model.Authentication;
namespace GUIClient.Tools.Track9;
public static class Track9WorkspaceAccess
{
    // These are the exact policy audiences. IsAdmin alone is not RequireRiskmanagement.
    public static bool CanReadRisk(AuthenticatedUserInfo? user) => user is not null &&
        (user.UserRole == "Administrator" || user.UserPermissions?.Contains("riskmanagement") == true);
    // RequireThirdPartyRead / RequireDataCatalogueRead / RequireAiGovernanceRead: the risk register's audience,
    // the Admin role claim (the admin flag, whatever the role is named) or the register's manage permission.
    public static bool CanReadRegister(AuthenticatedUserInfo? user, string permission) => user is not null &&
        (CanReadRisk(user) || AdminRoleClaim.IsHeldBy(user) || user.UserPermissions?.Contains(permission) == true);
    public static bool CanOpen(AuthenticatedUserInfo? user) =>
        CanReadRegister(user, "third_party_manage") || CanReadRegister(user, "data_catalogue_manage") ||
        CanReadRegister(user, "ai_governance_manage");
}
