using Model.Authentication;
using Model.DecisionCycle;

namespace GUIClient.Tools.Track9;

public static class Track9RegistersAccess
{
    public static bool IsThirdLine(AuthenticatedUserInfo? user) =>
        user?.UserPermissions?.Contains(ThirdLineAssurance.PermissionKey) == true;

    public static bool CanManageThirdParties(AuthenticatedUserInfo? user) =>
        CanManage(user, "third_party_manage");

    public static bool CanManageDataCatalogue(AuthenticatedUserInfo? user) =>
        CanManage(user, "data_catalogue_manage");

    public static bool CanManageAiGovernance(AuthenticatedUserInfo? user) =>
        CanManage(user, "ai_governance_manage");

    public static bool CanEditLegalRequirement(AuthenticatedUserInfo? user, bool thirdPartyHidden) =>
        !thirdPartyHidden && CanManageDataCatalogue(user);

    public static bool CanEditRiskLinks(AuthenticatedUserInfo? user) => user is not null &&
        !IsThirdLine(user) &&
        (user.UserRole == "Administrator" || user.UserPermissions?.Contains("riskmanagement") == true);

    // [PermissionAuthorize(permission)]: the Admin role claim (the admin flag, whatever the role is named) or the
    // permission — and never the third line, whose read-only requirement outranks both.
    private static bool CanManage(AuthenticatedUserInfo? user, string permission) => user is not null &&
        !IsThirdLine(user) &&
        (AdminRoleClaim.IsHeldBy(user) || user.UserPermissions?.Contains(permission) == true);
}
