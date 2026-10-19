using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;

namespace Model.RiskIndex;

/// <summary>
/// The module permissions the index's redaction consults (S40 §11.3): <c>risk_index_view</c> gives the index's values,
/// never the content of another module's record.
/// </summary>
public static class RiskIndexModulePermissions
{
    public const string RiskManagement = "riskmanagement";
    public const string Hosts = "hosts";
    public const string Vulnerabilities = "vulnerabilities";
    public const string Incidents = "incident_management";
    public const string Assessments = "assessments";

    /// <summary>The permissions whose possession changes a response — the cache key's permission part (S40 §8.1).</summary>
    public static readonly IReadOnlyList<string> All = [RiskManagement, Hosts, Vulnerabilities, Incidents, Assessments];
}

/// <summary>
/// Per-module redaction of the index's responses (S40 §5.0, §11.3), pure so the server applies it and a test pins it. A
/// caller without the module permission of an object's owner sees "‹Kind› #ref" with <c>RequiresPermission</c> set, and no
/// scenario inputs, decision state, signal text or navigation target of that module; the index's own values (R, points,
/// category, state, population, weight) stay. The administrator role passes, as it does on every module policy.
/// </summary>
public static class RiskIndexRedactionPolicy
{
    /// <summary>
    /// The permission that owns an object's label, or null when the label is the entity map's (visible to every
    /// authenticated user, S40 §7.4): scenarios and suppliers are the register's; hosts, external domains, vendor assets and
    /// tenants are the asset inventory's; an application, a target entity and the Unassigned group are named by the map.
    /// </summary>
    public static string? LabelPermission(RiskIndexObjectKind kind) => kind switch
    {
        RiskIndexObjectKind.Scenario or RiskIndexObjectKind.Supplier => RiskIndexModulePermissions.RiskManagement,
        RiskIndexObjectKind.Host or RiskIndexObjectKind.ExternalDomain or RiskIndexObjectKind.VendorAsset
            or RiskIndexObjectKind.Tenant => RiskIndexModulePermissions.Hosts,
        _ => null
    };

    /// <summary>
    /// The permission that owns a factor's signals (their text and links): findings are the vulnerability module's, a
    /// device score the host inventory's, incidents and assessments their modules', the register's factors the register's.
    /// </summary>
    public static string SignalPermission(string factor) => factor switch
    {
        _ when factor.StartsWith("EXP.V", StringComparison.Ordinal) => RiskIndexModulePermissions.Vulnerabilities,
        _ when factor.StartsWith("EXP.", StringComparison.Ordinal) => RiskIndexModulePermissions.Hosts,
        _ when factor.StartsWith("AME.", StringComparison.Ordinal) => RiskIndexModulePermissions.Incidents,
        _ when factor.StartsWith("CTL.A", StringComparison.Ordinal) => RiskIndexModulePermissions.Assessments,
        _ => RiskIndexModulePermissions.RiskManagement
    };

    public static bool Holds(string? permission, bool isAdmin, IReadOnlySet<string> permissions) =>
        permission is null || isAdmin || permissions.Contains(permission);

    public static bool CanSeeLabel(RiskIndexObjectKind kind, bool isAdmin, IReadOnlySet<string> permissions) =>
        Holds(LabelPermission(kind), isAdmin, permissions);

    /// <summary>The redacted label: "‹Kind› #ref".</summary>
    public static string RedactedLabel(RiskIndexObjectKind kind, string reference) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind} #{reference}");

    /// <summary>The label DTO, redacted when the caller lacks the owner's permission or no label is known.</summary>
    public static RiskIndexObjectLabelDto Label(RiskIndexObjectKind kind, string reference, string? label, bool isAdmin,
        IReadOnlySet<string> permissions)
    {
        var obj = new RiskIndexObjectRef(kind, reference);
        var permission = LabelPermission(kind);
        if (!Holds(permission, isAdmin, permissions)) return new RiskIndexObjectLabelDto(obj, RedactedLabel(kind, reference), permission);
        return new RiskIndexObjectLabelDto(obj, string.IsNullOrWhiteSpace(label) ? RedactedLabel(kind, reference) : label, null);
    }

    /// <summary>The module permissions the caller holds, sorted — with the administrator flag — for the cache key.</summary>
    public static string PermissionKey(bool isAdmin, IReadOnlySet<string> permissions) =>
        (isAdmin ? "admin|" : "") + string.Join(",", RiskIndexModulePermissions.All.Where(permissions.Contains).Order(StringComparer.Ordinal));
}
