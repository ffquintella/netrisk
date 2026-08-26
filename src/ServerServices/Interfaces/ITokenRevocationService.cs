namespace ServerServices.Interfaces;

/// <summary>
/// Per-token revocation, keyed on the JWT's <c>jti</c> claim (security finding NR-2026-028).
///
/// Track 7 delivered <em>mass</em> revocation — a password change invalidates every outstanding token
/// for the account, and disabling a user takes effect on the next request — but not "sign out this
/// one session", which cannot be expressed without per-token state. Tokens have carried a
/// <c>jti</c> since that track specifically so this could be added without another token-format
/// change.
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>
    /// Revokes one token. Idempotent: revoking twice is not an error, because the client that
    /// retried a failed sign-out did nothing wrong.
    /// </summary>
    Task RevokeAsync(string jti, int? userId, DateTime expiresAtUtc, string? reason = null);

    /// <summary>
    /// Whether a token has been revoked. Reads through a short-lived cache: this runs on every
    /// authenticated request, and an uncached indexed lookup per request is a cost the previous
    /// design did not have.
    /// </summary>
    Task<bool> IsRevokedAsync(string jti);

    /// <summary>
    /// Drops rows whose token has expired anyway. Returns how many went. Without this the table
    /// grows without bound, which is the usual reason a revocation list gets abandoned.
    /// </summary>
    Task<int> PruneExpiredAsync(DateTime asOfUtc);
}
