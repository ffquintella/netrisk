using DAL.Entities;
using Model.Findings;

namespace ServerServices.Interfaces;

/// <summary>
/// Issue, authenticate, list and revoke scoped CI API tokens (Track 3 milestone 3.5.1).
/// </summary>
public interface IApiTokensService
{
    /// <summary>
    /// Issues a token. The returned <see cref="IssuedApiToken.Secret"/> is the only time the secret
    /// exists outside the caller's hands — only its hash is stored.
    /// </summary>
    Task<IssuedApiToken> IssueAsync(string name, string scopes, int actsAsUserId, int? createdByUserId,
        DateTime? expiresAt = null, int? entityId = null, int? rateLimitPerMinute = null);

    /// <summary>
    /// Resolves a presented <c>nrk_…</c> token to its record, or null when it is malformed, unknown,
    /// revoked, or expired. Never says which — a caller holding a bad token learns only that it does
    /// not work.
    /// </summary>
    Task<ApiToken?> AuthenticateAsync(string presentedToken);

    /// <summary>
    /// Records that a token was used. Best-effort and deliberately not awaited on the auth path:
    /// a write failure here must never fail an otherwise valid request.
    /// </summary>
    Task TouchAsync(int tokenId, DateTime whenUtc);

    /// <summary>Tokens, newest first. Never includes a secret, because none is stored.</summary>
    Task<List<ApiToken>> GetTokensAsync(bool includeRevoked = false);

    Task<ApiToken> GetTokenAsync(int id);

    /// <summary>Revokes a token, immediately and irreversibly.</summary>
    Task<ApiToken> RevokeAsync(int id, int? revokedByUserId);
}
