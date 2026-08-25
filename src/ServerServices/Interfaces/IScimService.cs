using DAL.Entities;
using Model.Authentication.Scim;

namespace ServerServices.Interfaces;

/// <summary>
/// SCIM 2.0 provisioning (Track 4 milestone 4.3.2).
///
/// The service implements the resource semantics — filtering, pagination, RFC 7644 PATCH, and the
/// deactivation that actually disables login — and the controller does nothing but translate HTTP.
/// That split is what lets the PATCH semantics be tested without a web host, which matters because
/// PATCH is the operation both Entra ID and Okta use for the one thing that must not be wrong:
/// turning an account off.
/// </summary>
public interface IScimService
{
    // --- users ------------------------------------------------------------------------------

    /// <summary>
    /// Users, filtered and paged the SCIM way: <paramref name="startIndex"/> is 1-based, and
    /// <c>totalResults</c> counts before paging.
    ///
    /// Only <c>userName eq "…"</c> and <c>externalId eq "…"</c> are supported — the two filters an IdP
    /// actually sends. Anything else throws <see cref="Model.Exceptions.InvalidParameterException"/>,
    /// which the controller renders as SCIM <c>invalidFilter</c>; silently ignoring an unsupported
    /// filter would return the whole directory to a caller that asked for one user.
    /// </summary>
    Task<ScimListResponse<ScimUser>> ListUsersAsync(string? filter, int startIndex, int count);

    Task<ScimUser> GetUserAsync(string id);

    /// <summary>
    /// Creates a user. A duplicate <c>userName</c> throws
    /// <see cref="Model.Exceptions.InvalidParameterException"/>, which becomes SCIM 409 with
    /// <c>scimType: uniqueness</c> — the answer an IdP knows how to handle.
    /// </summary>
    Task<ScimUser> CreateUserAsync(ScimUser user);

    /// <summary>Full replace (PUT). Absent attributes are cleared, per the RFC.</summary>
    Task<ScimUser> ReplaceUserAsync(string id, ScimUser user);

    /// <summary>
    /// RFC 7644 PATCH. Supports <c>add</c>/<c>replace</c>/<c>remove</c> on the attributes NetRisk
    /// stores, including a path-less <c>replace</c> whose value is an object of attributes, which is
    /// what Entra ID sends.
    /// </summary>
    Task<ScimUser> PatchUserAsync(string id, ScimPatchRequest patch);

    /// <summary>
    /// SCIM DELETE. Deactivates rather than deleting the row: a NetRisk user is referenced by risks,
    /// findings and audit history, and hard-deleting them would either fail on a constraint or erase
    /// attribution. The IdP sees the resource gone, which is what it asked for.
    /// </summary>
    Task DeactivateUserAsync(string id);

    // --- groups -----------------------------------------------------------------------------

    Task<ScimListResponse<ScimGroup>> ListGroupsAsync(string? filter, int startIndex, int count);

    Task<ScimGroup> GetGroupAsync(string id);

    /// <summary>
    /// Creates a group. NetRisk has no group table of its own — a SCIM group is a NetRisk role — so
    /// this creates or adopts the role of the same name and applies the identity provider's group
    /// mapping to its members.
    /// </summary>
    Task<ScimGroup> CreateGroupAsync(ScimGroup group);

    Task<ScimGroup> ReplaceGroupAsync(string id, ScimGroup group);

    Task<ScimGroup> PatchGroupAsync(string id, ScimPatchRequest patch);

    Task DeleteGroupAsync(string id);

    // --- tokens -----------------------------------------------------------------------------

    /// <summary>
    /// Issues a provisioning credential. The returned view carries the secret exactly once; no read
    /// path can produce it again.
    /// </summary>
    Task<ScimTokenView> IssueTokenAsync(string name, int? identityProviderId, int? createdByUserId);

    Task<List<ScimTokenView>> GetTokensAsync(bool includeRevoked = false);

    Task<ScimTokenView> RevokeTokenAsync(int id, int? revokedByUserId);

    /// <summary>
    /// Authenticates a presented <c>scim_…</c> bearer token. Null for anything unusable — unknown,
    /// revoked, or wrong secret — deliberately without distinguishing them to the caller.
    /// </summary>
    Task<ScimToken?> AuthenticateAsync(string presentedToken);

    /// <summary>Records one SCIM request. Called for every request, including refused ones.</summary>
    Task LogRequestAsync(int? tokenId, string method, string path, int statusCode, string? target,
        string? outcome);

    /// <summary>The request audit, newest first.</summary>
    Task<List<ScimRequestLog>> GetRequestLogAsync(int limit = 200);
}
