namespace GUIClient.Tools.RiskIndex;

/// <summary>The stable <c>error</c> codes of the index's read API the dashboard branches on (S40 §8.1, §8.2).</summary>
public static class RiskApiCodes
{
    public const string InShadow = "risk_index_in_shadow";
    public const string OrgRequiresGlobalScope = "org_requires_global_scope";
    public const string NoSnapshot = "no_snapshot";
    public const string NodeNotFound = "node_not_found";
    public const string InvalidNode = "invalid_node";
    public const string SnapshotMembershipMissing = "snapshot_membership_missing";
    public const string DerivedBusy = "derived_computation_busy";
    public const string DerivedTimeout = "derived_computation_timeout";
    public const string InvalidPeriod = "invalid_period";
    public const string ProfileNotFound = "profile_not_found";
    public const string InvalidTab = "invalid_tab";
    public const string InvalidMonths = "invalid_months";
    public const string TrendUnavailablePartial = "trend_unavailable_partial";
    public const string WindowRequiresPrecomputedNode = "window_requires_precomputed_node";

    // The drill-down and the object list (T254).
    public const string InvalidDimension = "invalid_dimension";
    public const string InvalidFilter = "invalid_filter";
    public const string InvalidSort = "invalid_sort";
    public const string InvalidPage = "invalid_page";

    // What moved and the object detail (T255).
    public const string ChangesUnavailablePartial = "changes_unavailable_partial";
    public const string ChangesCrossProfile = "changes_cross_profile";
    public const string ObjectNotFound = "object_not_found";

    /// <summary>The codes the read path can answer, each with a <c>RiskApi_‹code›MSG</c> in the three resource files.</summary>
    public static readonly string[] ReadPath =
    [
        InShadow, OrgRequiresGlobalScope, NoSnapshot, NodeNotFound, InvalidNode, SnapshotMembershipMissing, DerivedBusy,
        DerivedTimeout, InvalidPeriod, ProfileNotFound, InvalidTab, InvalidMonths, TrendUnavailablePartial,
        WindowRequiresPrecomputedNode, InvalidDimension, InvalidFilter, InvalidSort, InvalidPage, ChangesUnavailablePartial,
        ChangesCrossProfile, ObjectNotFound
    ];
}

/// <summary>
/// What a refusal of the index API reads as (S40 §10.5): the translation of its code, <c>RiskApi_‹code›MSG</c>, and the
/// server's own sentence when no resource declares the code — never a generic "loading failed" that hides the reason.
/// </summary>
public static class RiskApiErrorText
{
    public static string ResourceKey(string code) => "RiskApi_" + code + "MSG";

    public static string Translate(string? code, string? serverMessage, TextLookup text, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(code) && text(ResourceKey(code)) is { Length: > 0 } translated) return translated;
        return string.IsNullOrWhiteSpace(serverMessage) ? fallback : serverMessage;
    }
}
