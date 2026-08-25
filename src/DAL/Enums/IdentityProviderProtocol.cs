namespace DAL.Enums;

/// <summary>
/// The federation protocol an identity-provider configuration speaks (Track 4 milestone 4.3.1),
/// persisted in <c>identity_providers.protocol</c>.
/// </summary>
public enum IdentityProviderProtocol
{
    /// <summary>OpenID Connect — authorization code with PKCE, configured from a discovery document.</summary>
    Oidc = 1,

    /// <summary>SAML 2.0 in the service-provider role, configured from IdP metadata.</summary>
    Saml2 = 2
}
