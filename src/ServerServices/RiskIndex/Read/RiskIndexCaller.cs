using System.Security.Claims;
using Model.RiskIndex;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// Who is reading or configuring the index, as the API authenticated them: the user, the administrator flag and the
/// permissions the token carries. The entity scope is not here: the services read it from <c>IDalService</c>, the one
/// source every query filter uses. The approval never trusts this record — it re-reads the approver's permissions from the
/// database (S40 §6.4, "permissão explícita").
/// </summary>
public sealed record RiskIndexCaller(int UserId, bool IsAdmin, IReadOnlySet<string> Permissions)
{
    /// <summary>The policy check: the permission, or the administrator role (<c>PermissionAuthorizationHandler</c>).</summary>
    public bool Has(string permission) => IsAdmin || Permissions.Contains(permission);

    /// <summary>The permission itself, never the administrator role.</summary>
    public bool HoldsExplicitly(string permission) => Permissions.Contains(permission);

    /// <summary>Builds the caller from the API's principal: the <c>Permission</c> claims and the <c>Admin</c> role claim.</summary>
    public static RiskIndexCaller FromPrincipal(int userId, ClaimsPrincipal? principal)
    {
        var permissions = principal?.Claims.Where(c => c.Type == "Permission").Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var admin = principal?.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin") == true;
        return new RiskIndexCaller(userId, admin, permissions);
    }

    /// <summary>The module-permission part of the read cache key (S40 §8.1).</summary>
    public string PermissionKey => RiskIndexRedactionPolicy.PermissionKey(IsAdmin, Permissions);
}

/// <summary>
/// A refusal of the index's API with its stable error code (S40 §8.1: <c>{ error, message, details? }</c>), mapped by the
/// controllers to <see cref="Status"/>. <see cref="RetryAfter"/> accompanies the 503 of the derived-computation budget.
/// </summary>
public sealed class RiskIndexException(int status, string code, string message,
    IReadOnlyDictionary<string, object?>? details = null, TimeSpan? retryAfter = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public static RiskIndexException BadRequest(string code, string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(400, code, message, details);

    public static RiskIndexException Forbidden(string code, string message) => new(403, code, message);

    public static RiskIndexException NotFound(string code, string message) => new(404, code, message);

    public static RiskIndexException Conflict(string code, string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(409, code, message, details);

    public static RiskIndexException Unavailable(string code, string message, TimeSpan retryAfter) =>
        new(503, code, message, null, retryAfter);
}

/// <summary>The error codes of S40 §8.2 and §8.4, stable: the desktop client translates each as <c>RiskApi_‹code›MSG</c>.</summary>
public static class RiskIndexErrors
{
    public const string InShadow = "risk_index_in_shadow";
    public const string InvalidNode = "invalid_node";
    public const string OrgRequiresGlobalScope = "org_requires_global_scope";
    public const string NoSnapshot = "no_snapshot";
    public const string NodeNotFound = "node_not_found";
    public const string SnapshotMembershipMissing = "snapshot_membership_missing";
    public const string TrendUnavailablePartial = "trend_unavailable_partial";
    public const string WindowRequiresPrecomputedNode = "window_requires_precomputed_node";
    public const string InvalidWindow = "invalid_window";
    public const string InvalidSeries = "invalid_series";
    public const string InvalidDimension = "invalid_dimension";
    public const string InvalidFilter = "invalid_filter";
    public const string InvalidSort = "invalid_sort";
    public const string InvalidPage = "invalid_page";
    public const string InvalidTab = "invalid_tab";
    public const string InvalidPeriod = "invalid_period";
    public const string ChangesUnavailablePartial = "changes_unavailable_partial";
    public const string ObjectNotFound = "object_not_found";
    public const string DerivedBusy = "derived_computation_busy";
    public const string DerivedTimeout = "derived_computation_timeout";
    public const string NoApprovedProfile = "no_approved_profile";

    public const string ProfileValuesRequireGlobalScope = "profile_values_require_global_scope";
    public const string ProfileNotFound = "profile_not_found";
    public const string OpenDraftExists = "open_draft_exists";
    public const string PresetRequiredForFirstProfile = "preset_required_for_first_profile";
    public const string PresetNotActivatable = "preset_not_activatable";
    public const string UnknownPreset = "unknown_preset";
    public const string StaleDraft = "stale_draft";
    public const string InvalidTransition = "invalid_transition";
    public const string IfMatchRequired = "if_match_required";
    public const string ParameterOutOfRange = "parameter_out_of_range";
    public const string NameRequired = "name_required";
    public const string PreviewInProgress = "preview_in_progress";
    public const string PreviewMissing = "preview_missing";
    public const string PreviewStale = "preview_stale";
    public const string JustificationRequired = "justification_required";
    public const string RebaselineReasonRequired = "rebaseline_reason_required";
    public const string ProfileInvalid = "profile_invalid";
    public const string SodAuthorCannotApprove = "sod_author_cannot_approve";
    public const string SodBusinessReviewer = "sod_business_reviewer";
    public const string ApproverPermissionNotExplicit = "approver_permission_not_explicit";
    public const string ApproverRoleRequired = "approver_role_required";
    public const string SodSelfGrantedApprover = "sod_self_granted_approver";
    public const string CommitteeAttachmentRequired = "committee_attachment_required";
    public const string FileNotFound = "file_not_found";
    public const string FileNotOwned = "file_not_owned";
    public const string FileAlreadyAttached = "file_already_attached";
    public const string CommitteeRefRequired = "committee_ref_required";
    public const string BoardRefRequired = "board_ref_required";
    public const string RebaselineConfirmationRequired = "rebaseline_confirmation_required";
    public const string ReasonRequired = "reason_required";
    public const string BridgeNotFound = "bridge_not_found";
    public const string InvalidLimit = "invalid_limit";
}
