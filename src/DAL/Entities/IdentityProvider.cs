using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One federated identity provider (Track 4 milestone 4.3.1) — an OIDC issuer or a SAML 2.0 IdP.
///
/// Several rows are supported deliberately: a tenant that has just acquired another company runs two
/// IdPs for a year, and a product that allows exactly one forces them to keep local passwords alive
/// for half the organization.
/// </summary>
public class IdentityProvider
{
    public int Id { get; set; }

    /// <summary>Human label shown on the sign-in choice — "Acme Entra ID".</summary>
    public string Name { get; set; } = null!;

    public IdentityProviderProtocol Protocol { get; set; }

    public bool Enabled { get; set; } = true;

    // --- OIDC ------------------------------------------------------------------------------

    /// <summary>OIDC issuer/authority. Discovery is read from <c>{Authority}/.well-known/openid-configuration</c>.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    /// <summary>
    /// Encrypted client secret. Optional: a desktop client is a public client and uses PKCE with no
    /// secret, which is the correct configuration rather than a degraded one.
    /// </summary>
    public string? EncryptedClientSecret { get; set; }

    /// <summary>Space-separated scopes. Defaults to <c>openid profile email</c> when null.</summary>
    public string? Scopes { get; set; }

    // --- SAML 2.0 --------------------------------------------------------------------------

    /// <summary>IdP metadata URL, fetched and cached.</summary>
    public string? MetadataUrl { get; set; }

    /// <summary>IdP metadata pasted as XML, for the many IdPs whose metadata is not reachable from the server.</summary>
    public string? MetadataXml { get; set; }

    /// <summary>The SP entity id NetRisk presents.</summary>
    public string? EntityIdValue { get; set; }

    /// <summary>Assertion consumer service URL registered with the IdP.</summary>
    public string? AssertionConsumerServiceUrl { get; set; }

    /// <summary>
    /// Signed assertions are required by default. Exposed as a column rather than hard-coded only
    /// because a test IdP sometimes cannot sign; turning it off is logged as a warning on every use.
    /// </summary>
    public bool RequireSignedAssertions { get; set; } = true;

    /// <summary>Tolerated clock skew when validating assertion/token time windows.</summary>
    public int ClockSkewSeconds { get; set; } = 120;

    /// <summary>Whether the IdP supports single logout, so the UI can offer it.</summary>
    public bool SupportsSingleLogout { get; set; }

    // --- Common ----------------------------------------------------------------------------

    /// <summary>
    /// Claim/attribute names to read the user's identity from, as JSON:
    /// <c>{"email":"...","name":"...","groups":"..."}</c>. Every IdP spells these differently and
    /// several spell them as URIs, so this cannot be a fixed set of columns.
    /// </summary>
    public string? ClaimMappingJson { get; set; }

    /// <summary>
    /// IdP group value → NetRisk role name / entity assignment, as JSON. Shared by SSO login and
    /// SCIM group provisioning so the two cannot disagree about what a group means.
    /// </summary>
    public string? GroupMappingJson { get; set; }

    /// <summary>
    /// Create a NetRisk user on first successful sign-in. Off by default: an IdP that authenticates
    /// the whole company would otherwise populate NetRisk with everyone who clicked the wrong tile.
    /// </summary>
    public bool JitProvisioning { get; set; }

    /// <summary>Role assigned to a JIT-created user when group mapping matches nothing.</summary>
    public int? DefaultRoleId { get; set; }

    /// <summary>Entity assigned to a JIT-created user when group mapping matches nothing.</summary>
    public int? DefaultEntityId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Role? DefaultRole { get; set; }

    public virtual Entity? DefaultEntity { get; set; }
}
