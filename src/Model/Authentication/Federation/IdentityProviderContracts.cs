using DAL.Enums;

namespace Model.Authentication.Federation;

/// <summary>
/// An identity provider as a client may see it (Track 4 milestone 4.3.1). No client secret, only a
/// flag saying whether one is configured.
/// </summary>
public class IdentityProviderView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public IdentityProviderProtocol Protocol { get; set; }

    public bool Enabled { get; set; }

    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public bool HasClientSecret { get; set; }

    public string? Scopes { get; set; }

    public string? MetadataUrl { get; set; }

    /// <summary>True when SAML metadata XML has been pasted in. The XML itself is not echoed back.</summary>
    public bool HasMetadataXml { get; set; }

    public string? EntityIdValue { get; set; }

    public string? AssertionConsumerServiceUrl { get; set; }

    public bool RequireSignedAssertions { get; set; }

    public int ClockSkewSeconds { get; set; }

    public bool SupportsSingleLogout { get; set; }

    public ClaimMapping ClaimMapping { get; set; } = new();

    public Dictionary<string, GroupMappingTarget> GroupMapping { get; set; } = new();

    public bool JitProvisioning { get; set; }

    public int? DefaultRoleId { get; set; }

    public int? DefaultEntityId { get; set; }
}

/// <summary>
/// Which claim or SAML attribute carries each piece of identity (Track 4 milestone 4.3.1).
///
/// Configurable rather than fixed because every IdP spells these differently and several spell them
/// as long URIs — Entra ID's email is often <c>preferred_username</c>, ADFS's is
/// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress</c>. A hard-coded set of
/// names works against exactly one IdP.
/// </summary>
public class ClaimMapping
{
    public string Email { get; set; } = "email";

    public string Name { get; set; } = "name";

    /// <summary>Claim used as the stable account identifier. Defaults to the subject.</summary>
    public string Subject { get; set; } = "sub";

    /// <summary>Claim carrying group membership. Multi-valued.</summary>
    public string Groups { get; set; } = "groups";

    /// <summary>Optional claim carrying the NetRisk login name, when it differs from the email.</summary>
    public string? Login { get; set; }
}

/// <summary>
/// What an IdP group maps to inside NetRisk (Track 4 milestones 4.3.1 and 4.3.2).
///
/// Shared by SSO login and SCIM group provisioning on purpose: "the Security-Admins group means
/// administrator" is one fact, and letting login and provisioning each hold their own copy of it is
/// how a user ends up with different permissions depending on which one touched them last.
/// </summary>
public class GroupMappingTarget
{
    /// <summary>NetRisk role name to assign. Null leaves the role alone.</summary>
    public string? Role { get; set; }

    /// <summary>Business entity id to assign. Null leaves the assignment alone.</summary>
    public int? EntityId { get; set; }

    /// <summary>Grant the administrator flag. Deliberately explicit, never inferred from a role name.</summary>
    public bool Admin { get; set; }
}

/// <summary>
/// The start of an OIDC or SAML sign-in (Track 4 milestone 4.3.1).
///
/// The desktop client opens <see cref="AuthorizationUrl"/> in the system browser and waits on the
/// loopback redirect; <see cref="State"/> and <see cref="CodeVerifier"/> are held by the server
/// against the state value so the client never has to keep PKCE material.
/// </summary>
public class FederatedSignInRequest
{
    public int ProviderId { get; set; }

    public string AuthorizationUrl { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    /// <summary>Where the IdP will redirect. Echoed so the client knows which loopback port to listen on.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Seconds the pending sign-in stays valid.</summary>
    public int ExpiresInSeconds { get; set; }
}

/// <summary>
/// The identity a completed federated sign-in produced, before NetRisk decides what to do with it
/// (Track 4 milestone 4.3.1).
/// </summary>
public class FederatedIdentity
{
    public string Subject { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Name { get; set; }

    public string? Login { get; set; }

    public List<string> Groups { get; set; } = new();

    /// <summary>Every claim the token or assertion carried, for diagnosing a mapping that matches nothing.</summary>
    public Dictionary<string, string> Claims { get; set; } = new();
}

/// <summary>
/// The outcome of completing a federated sign-in (Track 4 milestone 4.3.1).
///
/// A refusal is a value rather than an exception because there are several distinguishable ones —
/// the identity is valid but has no NetRisk account, JIT is off, the account is disabled — and the
/// client shows a different message for each.
/// </summary>
public class FederatedSignInResult
{
    public bool Success { get; init; }

    /// <summary>The NetRisk user id when the sign-in resolved to an account.</summary>
    public int? UserId { get; init; }

    public string? UserLogin { get; init; }

    /// <summary>True when this sign-in created the account through JIT provisioning.</summary>
    public bool Provisioned { get; init; }

    /// <summary>
    /// Set when the account is subject to the hardware-factor policy and has not completed it yet, so
    /// the caller must run the WebAuthn ceremony before issuing a session.
    /// </summary>
    public bool RequiresSecondFactor { get; init; }

    public string? Error { get; init; }

    public FederatedIdentity? Identity { get; init; }

    public static FederatedSignInResult Ok(int userId, string login, FederatedIdentity identity,
        bool provisioned = false, bool requiresSecondFactor = false) =>
        new()
        {
            Success = true, UserId = userId, UserLogin = login, Identity = identity,
            Provisioned = provisioned, RequiresSecondFactor = requiresSecondFactor
        };

    public static FederatedSignInResult Fail(string error, FederatedIdentity? identity = null) =>
        new() { Success = false, Error = error, Identity = identity };
}
