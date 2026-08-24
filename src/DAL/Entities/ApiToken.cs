namespace DAL.Entities;

/// <summary>
/// A non-interactive, scoped, revocable credential for CI pipelines (Track 3 milestone 3.5.1).
///
/// The secret itself is never stored — only <see cref="SecretHash"/>. A leaked database dump
/// therefore does not hand over working tokens, and there is no code path that can display a token
/// again after issue, which is deliberate: "show me the token again" is the feature that turns a
/// write-only secret into a readable one.
/// </summary>
public class ApiToken
{
    /// <summary>
    /// The <c>nrk_</c> prefix plus this key id is the public half of the token, stored in clear so
    /// a presented token can be looked up in one indexed read rather than by hashing against every
    /// row. It is also what makes a leaked token grep-able by secret scanners.
    /// </summary>
    public const string SecretPrefix = "nrk_";

    public int Id { get; set; }

    /// <summary>Human label — "github-actions-webapp". Not a secret.</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The public key id embedded in the presented token. Unique, random, and safe to log.
    /// </summary>
    public string KeyId { get; set; } = null!;

    /// <summary>Hash of the secret half. Never the secret.</summary>
    public string SecretHash { get; set; } = null!;

    /// <summary>
    /// Granted scopes, comma-separated (<c>vulnerabilities:import,vulnerabilities:read</c>). A
    /// token with no scopes can do nothing, which is the correct default for a mis-created token.
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>Null means no expiry. Discouraged but permitted for long-lived pipeline identities.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Restricts the token to one entity's data (composes with Track 2.3). Null means the token
    /// inherits the creating user's own scope.
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>
    /// The user this token acts as. Everything the token does is attributed to them, so a token is
    /// never more privileged than a person who can be asked about it.
    /// </summary>
    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>
    /// Updated opportunistically on use. Not part of authentication — an unwritable
    /// <c>last_used_at</c> must never fail a valid request — but it is what makes an unused token
    /// findable and revocable.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public int? RevokedById { get; set; }

    /// <summary>
    /// Requests per minute allowed for this token, or null for the system default. Per-token rather
    /// than per-user: one misconfigured pipeline should not exhaust a human's budget.
    /// </summary>
    public int? RateLimitPerMinute { get; set; }

    public virtual User? User { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? RevokedBy { get; set; }

    public virtual Entity? Entity { get; set; }

    /// <summary>True when the token is neither revoked nor past its expiry.</summary>
    public bool IsUsable(DateTime nowUtc) =>
        RevokedAt == null && (ExpiresAt == null || ExpiresAt > nowUtc);
}
