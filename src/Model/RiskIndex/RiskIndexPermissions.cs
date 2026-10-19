using System.Collections.Generic;
using Model.Entities;

namespace Model.RiskIndex;

/// <summary>The permissions of the Consolidated Risk Index (S40 §11.1), seeded with the index tables (T243).</summary>
public static class RiskIndexPermissions
{
    /// <summary>The dashboard and read API; reading profiles and, with unrestricted scope, previews, bridges and history.</summary>
    public const string View = "risk_index_view";

    /// <summary>Draft, validation, preview, submission, annotations and source editing.</summary>
    public const string Configure = "risk_index_configure";

    /// <summary>
    /// Approve, reject, attach evidence, suspend and publish — and see the index during the shadow pilot. Approval needs
    /// it explicitly, through an approver role: the administrator flag does not approve weights (S40 §11.4).
    /// </summary>
    public const string Approve = "risk_index_approve";
}

/// <summary>
/// The roles T293 seeds when absent (product-owner decision 2026-10-05): the Risk Manager and the Risk Administrator act
/// for the IT Risk Committee when approving profiles. Neither is an administrator role.
/// </summary>
public static class RiskIndexRoles
{
    /// <summary>Gerente de Riscos.</summary>
    public const string RiskManager = "RiskManager";

    /// <summary>Administrador de Riscos.</summary>
    public const string RiskAdministrator = "RiskAdministrator";

    /// <summary>The second-line author role seeded with the base schema (<c>Data/1.sql</c>).</summary>
    public const string RiskAnalyst = "RiskAnalyst";

    /// <summary>The seeded system administrator role, which this seed never grants an index permission.</summary>
    public const string Administrator = "Administrator";

    /// <summary>The default of <c>risk_index_approver_roles</c>: the two approver roles, comma-separated.</summary>
    public const string DefaultApproverRoles = RiskManager + "," + RiskAdministrator;

    /// <summary>What each seeded role receives (S40 §11.1). Administrator receives nothing: approval is never admin-implied.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SeededGrants =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [RiskManager] = [RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve,
                EntityPermissions.RiskContext],
            [RiskAdministrator] = [RiskIndexPermissions.View, RiskIndexPermissions.Configure, RiskIndexPermissions.Approve,
                EntityPermissions.RiskContext],
            [RiskAnalyst] = [RiskIndexPermissions.View, RiskIndexPermissions.Configure]
        };
}
