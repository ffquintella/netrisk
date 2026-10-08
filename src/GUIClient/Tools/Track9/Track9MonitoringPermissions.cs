using System;
using Model.Authentication;
using Model.DecisionCycle;

namespace GUIClient.Tools.Track9;

/// <summary>Desktop enablement mirroring the API policies. The API remains the authority for every write.</summary>
public static class Track9MonitoringPermissions
{
    private static readonly string[] ReviewPermissions =
        ["review_insignificant", "review_low", "review_medium", "review_high", "review_veryhigh"];

    public static bool IsThirdLine(AuthenticatedUserInfo? user) =>
        user?.UserPermissions?.Contains(ThirdLineAssurance.PermissionKey) == true;

    public static bool CanRead(AuthenticatedUserInfo? user) =>
        user is not null
        && (Role(user, "Administrator") || Has(user, "riskmanagement"));

    public static bool CanDefineKriOrCommittee(AuthenticatedUserInfo? user) =>
        Writable(user) && (Role(user!, "Admin") || Role(user!, "Administrator"));

    public static bool CanSubmitRiskData(AuthenticatedUserInfo? user) =>
        Writable(user) && Has(user!, "submit_risks");

    public static bool CanCloseRisk(AuthenticatedUserInfo? user) =>
        Writable(user) && (Role(user!, "Admin") || Has(user!, "close_risks"));

    public static bool CanReviewRisk(AuthenticatedUserInfo? user) =>
        Writable(user) && Array.Exists(ReviewPermissions, permission => Has(user!, permission));

    public static bool CanVote(AuthenticatedUserInfo? user, bool isCommitteeMember) =>
        Writable(user) && isCommitteeMember;

    private static bool Writable(AuthenticatedUserInfo? user) => user is not null && !IsThirdLine(user);
    private static bool Has(AuthenticatedUserInfo user, string permission) =>
        user.UserPermissions?.Contains(permission) == true;
    private static bool Role(AuthenticatedUserInfo user, string role) =>
        string.Equals(user.UserRole, role, StringComparison.Ordinal);
}
