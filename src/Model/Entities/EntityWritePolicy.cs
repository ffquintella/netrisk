namespace Model.Entities;

/// <summary>The permissions that gate writes to the entity map (T241, S40 §11.1).</summary>
public static class EntityPermissions
{
    /// <summary>Create, edit and delete entities (<c>POST</c>/<c>PUT</c>/<c>DELETE /Entities</c>).</summary>
    public const string Manage = "entities_manage";

    /// <summary>
    /// Set or change a <see cref="RiskContextProperties">risk-context property</see>, on create and on edit — and
    /// only with unrestricted scope (S40 §6.7 item 5).
    /// </summary>
    public const string RiskContext = "entity_risk_context";
}

/// <summary>
/// The entity properties that move the risk index's weights (S39 D-17, ENT-CRIT/ENT-INET/ENT-CLASS; S40 §6.7,
/// §11.1): criticality, internet exposure and security classification. Keyed by <b>property name</b>, whatever
/// the definition, so a property of one of these names that a later entity-configuration version adds — T242's
/// <c>internetFacing</c> on applications, <c>criticality</c> on more types — falls under the rule the day it
/// appears, with no code change. One list for the API's guard and the desktop's read-only fields.
/// </summary>
public static class RiskContextProperties
{
    public const string Criticality = "criticality";
    public const string InternetFacing = "internetFacing";
    public const string SecurityClassification = "securityClassification";

    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>(StringComparer.Ordinal) { Criticality, InternetFacing, SecurityClassification };

    public static bool IsRiskContext(string? propertyKey) => propertyKey is not null && Names.Contains(propertyKey);

    /// <summary>
    /// The risk-context properties whose values differ between <paramref name="stored"/> and
    /// <paramref name="incoming"/>, both given as (property key, value) pairs. A property absent from one side and
    /// blank on the other is unchanged; values compare trimmed and case-insensitively (a boolean saved as
    /// <c>True</c> and echoed back as <c>true</c> is the same value) and as sets, so the order of a multi-valued
    /// property does not matter. Anything else — set, cleared, changed — is a change.
    /// </summary>
    public static IReadOnlyList<string> Changed(IEnumerable<(string Key, string? Value)> stored,
        IEnumerable<(string Key, string? Value)> incoming)
    {
        var before = ValuesByKey(stored);
        var after = ValuesByKey(incoming);

        return Names
            .Where(name => !before.GetValueOrDefault(name, Empty).SetEquals(after.GetValueOrDefault(name, Empty)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static readonly HashSet<string> Empty = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, HashSet<string>> ValuesByKey(IEnumerable<(string Key, string? Value)> pairs) =>
        pairs
            .Where(p => IsRiskContext(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .GroupBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.Select(p => p.Value!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);
}

/// <summary>What a write to the entity map does, for <c>IEntitiesService.AuthorizeEntityWrite</c>.</summary>
public enum EntityWriteKind
{
    Create,
    Update,
    Delete
}

/// <summary>The machine-readable reasons the entity map refuses a write (S40 §8 error shape <c>{ error, message }</c>).</summary>
public static class EntityWriteErrors
{
    /// <summary>403: the body sets or changes a risk-context property without <see cref="EntityPermissions.RiskContext"/>.</summary>
    public const string RiskContextRequiresPermission = "risk_context_requires_permission";

    /// <summary>403: the caller holds the permission but a restricted scope (S40 §6.7 item 5).</summary>
    public const string RiskContextRequiresGlobalScope = "risk_context_requires_global_scope";

    /// <summary>403: a scoped caller names an entity, or a new parent, outside their subtree (T292).</summary>
    public const string EntityOutsideScope = "entity_outside_scope";

    /// <summary>403: a scoped caller creates a root, or moves an entity to the root.</summary>
    public const string RootRequiresGlobalScope = "entity_root_requires_global_scope";

    /// <summary>400: the new parent is the entity itself or one of its descendants.</summary>
    public const string ParentCycle = "entity_parent_cycle";

    /// <summary>400: the new parent does not exist.</summary>
    public const string ParentNotFound = "entity_parent_not_found";
}
