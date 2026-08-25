namespace DAL.Entities;

/// <summary>
/// A provisioning credential for one SCIM connection (Track 4 milestone 4.3.2).
///
/// Same write-only shape as <see cref="ApiToken"/> — public key id in clear, secret hashed, never
/// displayable again — because an IdP's provisioning token is a standing grant to create and disable
/// users, which makes it the most dangerous credential in the product.
/// </summary>
public class ScimToken
{
    /// <summary>Prefix on the presented credential, so the auth handler can tell it apart in one look.</summary>
    public const string SecretPrefix = "scim_";

    public int Id { get; set; }

    /// <summary>Human label — "Entra ID provisioning". Not a secret.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Public half, stored in clear so authentication is one indexed read.</summary>
    public string KeyId { get; set; } = null!;

    /// <summary>Hash of the secret half. Never the secret.</summary>
    public string SecretHash { get; set; } = null!;

    /// <summary>
    /// The identity provider whose claim/group mapping applies to users this token provisions. Null
    /// means the global mapping.
    /// </summary>
    public int? IdentityProviderId { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public int? RevokedById { get; set; }

    public virtual IdentityProvider? IdentityProvider { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? RevokedBy { get; set; }

    public bool IsUsable(DateTime nowUtc) => RevokedAt == null;
}
