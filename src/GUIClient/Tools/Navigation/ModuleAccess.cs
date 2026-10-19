using System;
using System.Linq;
using GUIClient.Models;
using Model.Authentication;

namespace GUIClient.Tools.Navigation;

/// <summary>
/// Who may open each shell module (S40 §10.3): the one rule the navigation bar enables a module's button by, read by the
/// bar, by the object detail's navigation buttons and by <c>INavigationService.NavigateTo</c>, so they cannot diverge.
/// Enabling only — the API decides every call. Anything not recognised (no user, no permission list) is "no".
/// </summary>
public static class ModuleAccess
{
    /// <summary>The permission a module's button needs; null for the dashboard and for the administrator-only modules.</summary>
    public static string? RequiredPermission(AvaliableViews view) => view switch
    {
        AvaliableViews.Devices => "hosts",
        AvaliableViews.Assessment => "assessments",
        AvaliableViews.Risk => "riskmanagement",
        AvaliableViews.Entities => "asset",
        AvaliableViews.Vulnerabilities => "vulnerabilities",
        AvaliableViews.Incidents => "incident_management",
        AvaliableViews.RiskOverview => "risk_index_view",
        _ => null
    };

    /// <summary>
    /// Whether <paramref name="user"/> may open <paramref name="view"/>: the permission or the admin flag, as the bar always
    /// did; the Cyber Risk Overview also admits the <c>Admin</c> role (<c>RiskIndex.RiskIndexAccess</c>); the Master
    /// Dashboard is the admin flag's alone. Users has no module in the shell, so it is never a destination.
    /// </summary>
    public static bool CanOpen(AvaliableViews view, AuthenticatedUserInfo? user)
    {
        if (user is null) return false;
        return view switch
        {
            AvaliableViews.Dashboard => true,
            AvaliableViews.MasterDashboard => user.IsAdmin,
            AvaliableViews.RiskOverview => RiskIndex.RiskIndexAccess.CanView(user),
            AvaliableViews.Users => false,
            _ when RequiredPermission(view) is { } permission => user.IsAdmin || Holds(user, permission),
            _ => false
        };
    }

    /// <summary>The resource key that names the module in the navigation bar.</summary>
    public static string NameKey(AvaliableViews view) => view switch
    {
        AvaliableViews.MasterDashboard => "Master Dashboard",
        AvaliableViews.Devices => "Devices",
        AvaliableViews.Assessment => "Assessment",
        AvaliableViews.Risk => "Risks",
        AvaliableViews.Users => "Users",
        AvaliableViews.Entities => "Entities",
        AvaliableViews.Vulnerabilities => "Vulnerabilities",
        AvaliableViews.Incidents => "Incidents",
        AvaliableViews.RiskOverview => "NavRiskOverview",
        _ => "Dashboard"
    };

    private static bool Holds(AuthenticatedUserInfo user, string permission) =>
        user.UserPermissions?.Any(p => string.Equals(p, permission, StringComparison.Ordinal)) ?? false;
}
