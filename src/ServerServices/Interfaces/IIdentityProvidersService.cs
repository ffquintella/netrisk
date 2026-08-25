using DAL.Entities;
using Model.Authentication.Federation;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// Federated sign-in configuration and the OIDC/SAML flows themselves
/// (Track 4 milestone 4.3.1).
///
/// The flows are explicit calls rather than ASP.NET Core authentication middleware because the
/// primary client is a desktop application: it opens the system browser, the IdP redirects to a
/// loopback URL, and the client posts the authorization code back for exchange. Cookie-based
/// middleware has nowhere to put a cookie in that shape.
/// </summary>
public interface IIdentityProvidersService
{
    Task<List<IdentityProviderView>> GetProvidersAsync(bool includeDisabled = true);

    /// <summary>
    /// The providers a sign-in screen may offer: enabled only, and only the fields needed to render a
    /// button. Safe to serve to an unauthenticated caller.
    /// </summary>
    Task<List<IdentityProviderView>> GetEnabledForSignInAsync();

    Task<IdentityProviderView> GetProviderAsync(int id);

    Task<IdentityProviderView> CreateProviderAsync(IdentityProvider provider, string? clientSecret);

    /// <summary>A null <paramref name="clientSecret"/> leaves the stored one alone.</summary>
    Task<IdentityProviderView> UpdateProviderAsync(IdentityProvider provider, string? clientSecret);

    Task DeleteProviderAsync(int id);

    /// <summary>
    /// Verifies the provider is usable: the OIDC discovery document is reachable and names the
    /// endpoints, or the SAML metadata parses and carries a signing certificate.
    /// </summary>
    Task<ConnectionTestResult> TestProviderAsync(int id);

    // --- OIDC ------------------------------------------------------------------------------

    /// <summary>
    /// Begins an authorization-code-with-PKCE sign-in. The verifier stays on the server, keyed by the
    /// returned state, so a client that cannot keep a secret does not have to.
    /// </summary>
    Task<FederatedSignInRequest> BeginOidcSignInAsync(int providerId, string redirectUri);

    /// <summary>
    /// Exchanges the authorization code, validates the id_token against the IdP's JWKS, maps the
    /// claims, and resolves or provisions the NetRisk account.
    ///
    /// The state must match a pending sign-in; a replayed or unknown state is refused, which is what
    /// makes the flow resistant to an injected authorization code.
    /// </summary>
    Task<FederatedSignInResult> CompleteOidcSignInAsync(string state, string code);

    // --- SAML 2.0 ---------------------------------------------------------------------------

    /// <summary>
    /// Builds an SP-initiated <c>AuthnRequest</c> for the HTTP-Redirect binding and returns the URL to
    /// open, plus the request id held for <c>InResponseTo</c> validation.
    /// </summary>
    Task<FederatedSignInRequest> BeginSamlSignInAsync(int providerId, string? relayState = null);

    /// <summary>
    /// Validates a SAML response — signature, conditions, audience and <c>InResponseTo</c> — maps its
    /// attributes and resolves or provisions the account.
    /// </summary>
    Task<FederatedSignInResult> CompleteSamlSignInAsync(string base64SamlResponse, string? relayState);

    /// <summary>
    /// The SP metadata NetRisk publishes, so an administrator can hand it to the IdP rather than
    /// retyping entity ids and ACS URLs.
    /// </summary>
    Task<string> GetServiceProviderMetadataAsync(int providerId);
}
