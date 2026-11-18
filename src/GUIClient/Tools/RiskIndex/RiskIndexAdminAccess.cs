using System;
using System.Linq;
using Model.Authentication;
using Model.Entities;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The six tabs of the Risk index administration pane (S40 §6.0).</summary>
public enum RiskIndexAdminTab
{
    Profiles = 0,
    Draft = 1,
    Preview = 2,
    Approval = 3,
    Sources = 4,
    RiskContext = 5
}

/// <summary>Whether a tab is open, whether it only reads, and the resource key of the reason when it is closed.</summary>
public sealed record RiskIndexTabAccess(bool Enabled, bool ReadOnly, string? DisabledReasonKey);

/// <summary>
/// The permissions the administration window reads, from the signed-in user (S40 §6.0). The <c>Admin</c> role claim passes
/// every <c>[PermissionAuthorize]</c> on the API, so it opens every tab here too; the approval itself re-reads explicit
/// grants on the server (§11.4), which is why the Approve button follows the server's checks and not this record.
/// </summary>
public sealed record RiskIndexAdminRights(bool IsAdmin, bool View, bool Configure, bool Approve, bool RiskContext,
    bool EntitiesManage, bool Integrations = false)
{
    /// <summary>The API's integration permission, which <c>/Integrations/EntityMappings</c> requires (S40 §8.6; T263).</summary>
    public const string IntegrationsPermission = "configuration";

    public static RiskIndexAdminRights From(AuthenticatedUserInfo? user)
    {
        if (user is null) return new RiskIndexAdminRights(false, false, false, false, false, false);
        var admin = AdminRoleClaim.IsHeldBy(user);
        bool Holds(string permission) =>
            admin || (user.UserPermissions?.Any(p => string.Equals(p, permission, StringComparison.Ordinal)) ?? false);
        return new RiskIndexAdminRights(admin, Holds(RiskIndexPermissions.View), Holds(RiskIndexPermissions.Configure),
            Holds(RiskIndexPermissions.Approve), Holds(EntityPermissions.RiskContext), Holds(EntityPermissions.Manage),
            Holds(IntegrationsPermission));
    }

    /// <summary>The Administration button and the Risk index entry: any of the four (S40 §6.0).</summary>
    public bool CanOpenRiskIndex => IsAdmin || Configure || Approve || RiskContext;
}

/// <summary>The tab rules of S40 §6.0 as a pure function, tested in <c>GUIClient.Tests</c> (§12.4).</summary>
public static class RiskIndexAdminAccess
{
    public const string RequiresView = "RiskAdminRequiresView";
    public const string RequiresConfigure = "RiskAdminRequiresConfigure";
    public const string RequiresApprove = "RiskAdminRequiresApprove";
    public const string RequiresRiskContext = "RiskAdminRequiresRiskContext";

    /// <summary>Whether the Administration window opens at all for this user.</summary>
    public static bool CanOpenAdministration(RiskIndexAdminRights rights) => rights.IsAdmin || rights.CanOpenRiskIndex;

    public static RiskIndexTabAccess For(RiskIndexAdminTab tab, RiskIndexAdminRights rights)
    {
        ArgumentNullException.ThrowIfNull(rights);
        if (!rights.CanOpenRiskIndex) return new RiskIndexTabAccess(false, true, RequiresConfigure);

        return tab switch
        {
            // The context tab reads only /Entities, so it needs no risk_index_view (§6.0 table).
            RiskIndexAdminTab.RiskContext => new RiskIndexTabAccess(true, !rights.RiskContext, null),
            _ when !rights.View => new RiskIndexTabAccess(false, true, RequiresView),
            RiskIndexAdminTab.Profiles or RiskIndexAdminTab.Sources => new RiskIndexTabAccess(true, !rights.Configure, null),
            RiskIndexAdminTab.Draft or RiskIndexAdminTab.Preview => rights.Configure || rights.Approve
                ? new RiskIndexTabAccess(true, !rights.Configure, null)
                : new RiskIndexTabAccess(false, true, RequiresConfigure),
            RiskIndexAdminTab.Approval => rights.Approve || rights.Configure
                ? new RiskIndexTabAccess(true, !rights.Approve, null)
                : new RiskIndexTabAccess(false, true, RequiresApprove),
            _ => new RiskIndexTabAccess(false, true, RequiresView)
        };
    }

    /// <summary>The tab the pane opens on: the first one the user may open, in tab order.</summary>
    public static RiskIndexAdminTab FirstOpen(RiskIndexAdminRights rights) =>
        Enum.GetValues<RiskIndexAdminTab>().FirstOrDefault(t => For(t, rights).Enabled, RiskIndexAdminTab.RiskContext);
}
