namespace DAL.Entities;

/// <summary>
/// A single revoked bearer token, keyed on its <c>jti</c> (security finding NR-2026-028).
///
/// Mass revocation already existed — a password change invalidates every outstanding token for the
/// account, and disabling a user takes effect on the next request. What was missing was "sign out
/// this one session", which cannot be expressed without per-token state. Rows are pruned past
/// <see cref="ExpiresAt"/>, so the table's size is bounded by the token lifetime rather than by
/// history.
/// </summary>
public class RevokedToken
{
    public int Id { get; set; }

    /// <summary>The token's <c>jti</c> claim. Unique: revoking twice is not an error.</summary>
    public string Jti { get; set; } = null!;

    public int? UserId { get; set; }

    public DateTime RevokedAt { get; set; }

    /// <summary>The token's own <c>exp</c>. After this instant the row is dead weight.</summary>
    public DateTime ExpiresAt { get; set; }

    public string? Reason { get; set; }

    public virtual User? User { get; set; }
}
