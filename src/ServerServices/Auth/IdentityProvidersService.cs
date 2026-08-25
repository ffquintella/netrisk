using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Model.Authentication.Federation;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Auth;

/// <summary>
/// Federated sign-in: provider configuration, the OIDC authorization-code flow with PKCE, SAML 2.0
/// SP-initiated sign-in, claim/group mapping and optional JIT provisioning
/// (Track 4 milestone 4.3.1).
/// </summary>
public class IdentityProvidersService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    IOutboundHttpClient http,
    PendingFederatedSignIns pending,
    Microsoft.Extensions.Configuration.IConfiguration configuration)
    : ServiceBase(logger, dalService), IIdentityProvidersService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Discovery documents and their JWKS, cached per authority for
    /// <see cref="DiscoveryCacheLifetime"/>. Refetching on every sign-in would add two round trips to
    /// the IdP to every login and, on a busy tenant, look like abuse.
    /// </summary>
    private static readonly Dictionary<string, (OpenIdConnectConfiguration Configuration, DateTime FetchedAt)>
        Discovery = new();

    private static readonly object DiscoveryLock = new();

    /// <summary>
    /// How long a discovery document is reused. An hour is long enough to matter and short enough that
    /// a key rotation is picked up without an operator having to restart anything.
    /// </summary>
    internal static readonly TimeSpan DiscoveryCacheLifetime = TimeSpan.FromHours(1);

    // --- configuration ----------------------------------------------------------------------

    public async Task<List<IdentityProviderView>> GetProvidersAsync(bool includeDisabled = true)
    {
        await using var db = DalService.GetContext();

        var providers = await db.IdentityProviders
            .Where(p => includeDisabled || p.Enabled)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return providers.Select(ToView).ToList();
    }

    public async Task<List<IdentityProviderView>> GetEnabledForSignInAsync()
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var providers = await db.IdentityProviders
            .Where(p => p.Enabled)
            .OrderBy(p => p.Name)
            .ToListAsync();

        // Only what a sign-in button needs. The claim and group mappings are configuration, not
        // something an anonymous caller should be able to enumerate.
        return providers.Select(p => new IdentityProviderView
        {
            Id = p.Id,
            Name = p.Name,
            Protocol = p.Protocol,
            Enabled = true
        }).ToList();
    }

    public async Task<IdentityProviderView> GetProviderAsync(int id)
    {
        await using var db = DalService.GetContext();
        return ToView(await LoadAsync(db, id));
    }

    public async Task<IdentityProviderView> CreateProviderAsync(IdentityProvider provider, string? clientSecret)
    {
        Validate(provider);

        await using var db = DalService.GetContext();

        if (await db.IdentityProviders.AnyAsync(p => p.Name == provider.Name))
            throw new InvalidParameterException(nameof(provider.Name),
                $"An identity provider named '{provider.Name}' already exists.");

        var stored = Copy(provider, new IdentityProvider { CreatedAt = DateTime.UtcNow });
        stored.EncryptedClientSecret = protector.Protect(clientSecret);

        db.IdentityProviders.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("Identity provider {Name} ({Protocol}) created", stored.Name, stored.Protocol);

        return ToView(stored);
    }

    public async Task<IdentityProviderView> UpdateProviderAsync(IdentityProvider provider, string? clientSecret)
    {
        Validate(provider);

        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, provider.Id);

        if (await db.IdentityProviders.AnyAsync(p => p.Name == provider.Name && p.Id != provider.Id))
            throw new InvalidParameterException(nameof(provider.Name),
                $"An identity provider named '{provider.Name}' already exists.");

        Copy(provider, stored);
        stored.UpdatedAt = DateTime.UtcNow;

        if (clientSecret != null) stored.EncryptedClientSecret = protector.Protect(clientSecret);

        await db.SaveChangesAsync();

        // The cached discovery document belongs to the old authority; dropping it is cheaper than
        // reasoning about whether the authority changed.
        InvalidateDiscovery(stored.Authority);

        return ToView(stored);
    }

    public async Task DeleteProviderAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);

        db.IdentityProviders.Remove(stored);
        await db.SaveChangesAsync();

        Logger.Information("Identity provider {Id} ({Name}) deleted", id, stored.Name);
    }

    public async Task<ConnectionTestResult> TestProviderAsync(int id)
    {
        await using var db = DalService.GetContext();

        var provider = await LoadAsync(db, id);

        try
        {
            if (provider.Protocol == IdentityProviderProtocol.Oidc)
            {
                var discovery = await GetDiscoveryAsync(provider);

                var details = new Dictionary<string, string>
                {
                    ["Issuer"] = discovery.Issuer ?? "(none)",
                    ["Authorization endpoint"] = discovery.AuthorizationEndpoint ?? "(none)",
                    ["Token endpoint"] = discovery.TokenEndpoint ?? "(none)",
                    ["Signing keys"] = discovery.SigningKeys.Count.ToString()
                };

                if (string.IsNullOrEmpty(discovery.AuthorizationEndpoint)
                    || string.IsNullOrEmpty(discovery.TokenEndpoint))
                    return ConnectionTestResult.Fail(
                        "The discovery document is readable but names no authorization or token endpoint.");

                if (discovery.SigningKeys.Count == 0)
                    return ConnectionTestResult.Fail(
                        "The discovery document names no signing keys, so an id_token could not be validated.");

                return ConnectionTestResult.Ok(
                    $"Read the discovery document for issuer '{discovery.Issuer}'.", details);
            }

            var (metadata, error) = await ResolveSamlMetadataAsync(provider);

            if (error != null) return ConnectionTestResult.Fail(error);

            return ConnectionTestResult.Ok(
                $"Parsed the identity provider metadata for '{metadata.EntityId}'.",
                new Dictionary<string, string>
                {
                    ["Entity id"] = metadata.EntityId ?? "(none)",
                    ["SSO URL"] = metadata.SsoUrl ?? "(none)",
                    ["Signing certificates"] = metadata.Certificates.Count.ToString()
                });
        }
        catch (Exception ex)
        {
            Logger.Warning("Testing identity provider {Id} threw: {Message}", id, ex.Message);
            return ConnectionTestResult.Fail($"The test failed: {ex.Message}");
        }
    }

    // --- OIDC ------------------------------------------------------------------------------

    public async Task<FederatedSignInRequest> BeginOidcSignInAsync(int providerId, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new InvalidParameterException(nameof(redirectUri), "A redirect URI is required.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var provider = await LoadAsync(db, providerId);

        if (!provider.Enabled)
            throw new InvalidParameterException(nameof(providerId), $"Provider '{provider.Name}' is disabled.");

        if (provider.Protocol != IdentityProviderProtocol.Oidc)
            throw new InvalidParameterException(nameof(providerId),
                $"Provider '{provider.Name}' is a SAML provider; use the SAML sign-in endpoint.");

        if (!IsAllowedRedirect(redirectUri))
            throw new InvalidParameterException(nameof(redirectUri),
                "The redirect URI must be a loopback address (the desktop client's local listener) or one "
                + "of the URIs configured in app:allowedRedirectUris.");

        var discovery = await GetDiscoveryAsync(provider);

        if (string.IsNullOrEmpty(discovery.AuthorizationEndpoint))
            throw new IntegrationRequestException(provider.Name,
                "The identity provider's discovery document names no authorization endpoint.");

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        pending.Add(new PendingSignIn
        {
            State = state,
            ProviderId = providerId,
            CodeVerifier = verifier,
            RedirectUri = redirectUri
        });

        var scopes = string.IsNullOrWhiteSpace(provider.Scopes) ? "openid profile email" : provider.Scopes;

        var url = discovery.AuthorizationEndpoint
                  + (discovery.AuthorizationEndpoint.Contains('?') ? "&" : "?")
                  + "response_type=code"
                  + "&client_id=" + Uri.EscapeDataString(provider.ClientId ?? string.Empty)
                  + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                  + "&scope=" + Uri.EscapeDataString(scopes)
                  + "&state=" + Uri.EscapeDataString(state)
                  + "&code_challenge=" + Uri.EscapeDataString(challenge)
                  + "&code_challenge_method=S256";

        return new FederatedSignInRequest
        {
            ProviderId = providerId,
            AuthorizationUrl = url,
            State = state,
            RedirectUri = redirectUri,
            ExpiresInSeconds = (int)PendingFederatedSignIns.Lifetime.TotalSeconds
        };
    }

    public async Task<FederatedSignInResult> CompleteOidcSignInAsync(string state, string code)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
            return FederatedSignInResult.Fail("The sign-in is missing its state or authorization code.");

        var entry = pending.TryRedeem(state, DateTime.UtcNow);

        // Unknown state covers three cases at once — expired, already redeemed, and never issued — and
        // all three must be refused. This is what makes an injected authorization code useless.
        if (entry == null)
            return FederatedSignInResult.Fail(
                "This sign-in is no longer valid. Start the sign-in again.");

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var provider = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == entry.ProviderId);

        if (provider is not { Enabled: true })
            return FederatedSignInResult.Fail("The identity provider is no longer available.");

        var discovery = await GetDiscoveryAsync(provider);

        if (string.IsNullOrEmpty(discovery.TokenEndpoint))
            return FederatedSignInResult.Fail("The identity provider names no token endpoint.");

        var form = new List<string>
        {
            "grant_type=authorization_code",
            "code=" + Uri.EscapeDataString(code),
            "redirect_uri=" + Uri.EscapeDataString(entry.RedirectUri ?? string.Empty),
            "client_id=" + Uri.EscapeDataString(provider.ClientId ?? string.Empty),
            "code_verifier=" + Uri.EscapeDataString(entry.CodeVerifier ?? string.Empty)
        };

        var headers = new Dictionary<string, string>();
        var secret = protector.Unprotect(provider.EncryptedClientSecret);

        // A confidential client authenticates with basic auth at the token endpoint; a public client
        // (the desktop flow) authenticates with PKCE alone, which is correct and not a downgrade.
        if (!string.IsNullOrEmpty(secret))
            headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{provider.ClientId}:{secret}"));

        var response = await http.SendAsync(new OutboundHttpRequest
        {
            Method = "POST",
            Url = discovery.TokenEndpoint,
            Body = string.Join("&", form),
            ContentType = "application/x-www-form-urlencoded",
            Headers = headers
        });

        if (!response.IsSuccess)
        {
            Logger.Warning("Token exchange with {Provider} failed: HTTP {Status}", provider.Name,
                response.StatusCode);

            return FederatedSignInResult.Fail(
                $"The identity provider refused the authorization code (HTTP {response.StatusCode}).");
        }

        string? idToken;

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            idToken = document.RootElement.TryGetProperty("id_token", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return FederatedSignInResult.Fail("The identity provider's token response was not valid JSON.");
        }

        if (string.IsNullOrEmpty(idToken))
            return FederatedSignInResult.Fail(
                "The identity provider returned no id_token. Check that the 'openid' scope is requested "
                + "and granted.");

        var identity = ValidateIdToken(provider, discovery, idToken, out var validationError);

        if (identity == null) return FederatedSignInResult.Fail(validationError!);

        return await ResolveAccountAsync(provider, identity);
    }

    /// <summary>
    /// Validates the id_token against the IdP's JWKS and maps its claims.
    ///
    /// Issuer, audience, lifetime and signature are all validated; the audience is the client id,
    /// which is what stops a token minted for another application at the same IdP from signing in here.
    /// </summary>
    internal FederatedIdentity? ValidateIdToken(IdentityProvider provider,
        OpenIdConnectConfiguration discovery, string idToken, out string? error)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            ValidateIssuer = !string.IsNullOrEmpty(discovery.Issuer),
            ValidAudience = provider.ClientId,
            ValidateAudience = !string.IsNullOrEmpty(provider.ClientId),
            IssuerSigningKeys = discovery.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Clamp(provider.ClockSkewSeconds, 0, 3600)),
            RequireSignedTokens = true
        };

        try
        {
            handler.ValidateToken(idToken, parameters, out var validated);

            var jwt = (JwtSecurityToken)validated;
            var mapping = ParseClaimMapping(provider.ClaimMappingJson);

            var identity = new FederatedIdentity();

            foreach (var claim in jwt.Claims)
            {
                // Multi-valued claims (groups, roles) arrive as repeated claims, so they are joined for
                // the diagnostic dump and collected individually below.
                identity.Claims[claim.Type] = identity.Claims.TryGetValue(claim.Type, out var existing)
                    ? existing + ", " + claim.Value
                    : claim.Value;

                if (string.Equals(claim.Type, mapping.Groups, StringComparison.OrdinalIgnoreCase))
                    identity.Groups.Add(claim.Value);
            }

            identity.Subject = identity.Claims.GetValueOrDefault(mapping.Subject) ?? jwt.Subject;
            identity.Email = identity.Claims.GetValueOrDefault(mapping.Email);
            identity.Name = identity.Claims.GetValueOrDefault(mapping.Name);
            identity.Login = mapping.Login == null ? null : identity.Claims.GetValueOrDefault(mapping.Login);

            error = null;
            return identity;
        }
        catch (SecurityTokenException ex)
        {
            // The specific reason matters to an administrator: an audience mismatch and an expired
            // token are different mistakes.
            error = $"The id_token was rejected: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            error = $"The id_token could not be validated: {ex.Message}";
            return null;
        }
    }

    // --- SAML ------------------------------------------------------------------------------

    public async Task<FederatedSignInRequest> BeginSamlSignInAsync(int providerId, string? relayState = null)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var provider = await LoadAsync(db, providerId);

        if (!provider.Enabled)
            throw new InvalidParameterException(nameof(providerId), $"Provider '{provider.Name}' is disabled.");

        if (provider.Protocol != IdentityProviderProtocol.Saml2)
            throw new InvalidParameterException(nameof(providerId),
                $"Provider '{provider.Name}' is an OIDC provider; use the OIDC sign-in endpoint.");

        var (metadata, error) = await ResolveSamlMetadataAsync(provider);

        if (error != null) throw new IntegrationRequestException(provider.Name, error);

        if (string.IsNullOrEmpty(metadata.SsoUrl))
            throw new IntegrationRequestException(provider.Name,
                "The identity provider metadata names no SingleSignOnService endpoint.");

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        var (url, requestId) = SamlAssertion.BuildAuthnRequestUrl(
            metadata.SsoUrl,
            provider.EntityIdValue ?? DefaultEntityId(),
            provider.AssertionConsumerServiceUrl ?? DefaultAcsUrl(provider.Id),
            relayState ?? state,
            DateTime.UtcNow);

        pending.Add(new PendingSignIn
        {
            State = state,
            ProviderId = providerId,
            RequestId = requestId
        });

        return new FederatedSignInRequest
        {
            ProviderId = providerId,
            AuthorizationUrl = url,
            State = state,
            RedirectUri = provider.AssertionConsumerServiceUrl ?? DefaultAcsUrl(provider.Id),
            ExpiresInSeconds = (int)PendingFederatedSignIns.Lifetime.TotalSeconds
        };
    }

    public async Task<FederatedSignInResult> CompleteSamlSignInAsync(string base64SamlResponse, string? relayState)
    {
        if (string.IsNullOrWhiteSpace(base64SamlResponse))
            return FederatedSignInResult.Fail("The SAML response was empty.");

        // RelayState carries the state value NetRisk generated, which is how the response is tied back
        // to the request whose id must appear in InResponseTo.
        var entry = relayState == null ? null : pending.TryRedeem(relayState, DateTime.UtcNow);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        IdentityProvider? provider = null;

        if (entry != null)
        {
            provider = await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == entry.ProviderId);
        }
        else
        {
            // An IdP-initiated response has no RelayState NetRisk issued. It is accepted only when
            // exactly one SAML provider is configured, because with two there is no way to know which
            // one's certificate should verify it — and trying them all would let a compromised IdP
            // impersonate users of the other.
            var samlProviders = await db.IdentityProviders
                .Where(p => p.Enabled && p.Protocol == IdentityProviderProtocol.Saml2)
                .ToListAsync();

            if (samlProviders.Count == 1) provider = samlProviders[0];
        }

        if (provider is not { Enabled: true })
            return FederatedSignInResult.Fail(
                "The SAML response could not be matched to a configured identity provider. If this was an "
                + "IdP-initiated sign-in and more than one SAML provider is configured, start the sign-in "
                + "from NetRisk instead.");

        var (metadata, error) = await ResolveSamlMetadataAsync(provider);

        if (error != null) return FederatedSignInResult.Fail(error);

        var outcome = SamlAssertion.Validate(base64SamlResponse, metadata.Certificates,
            provider.EntityIdValue ?? DefaultEntityId(), entry?.RequestId,
            ParseClaimMapping(provider.ClaimMappingJson), provider.RequireSignedAssertions,
            provider.ClockSkewSeconds, DateTime.UtcNow);

        if (!provider.RequireSignedAssertions)
            Logger.Warning(
                "Identity provider {Provider} accepts unsigned assertions. Any party that can reach the "
                + "ACS endpoint can therefore assert any identity.", provider.Name);

        if (!outcome.Valid || outcome.Identity == null)
            return FederatedSignInResult.Fail(outcome.Error ?? "The SAML assertion was rejected.",
                outcome.Identity);

        return await ResolveAccountAsync(provider, outcome.Identity);
    }

    public async Task<string> GetServiceProviderMetadataAsync(int providerId)
    {
        await using var db = DalService.GetContext();

        var provider = await LoadAsync(db, providerId);

        var entityId = provider.EntityIdValue ?? DefaultEntityId();
        var acs = provider.AssertionConsumerServiceUrl ?? DefaultAcsUrl(provider.Id);

        // No signing or encryption key is advertised: NetRisk does not sign AuthnRequests, and
        // advertising a key it does not use would make an IdP require signatures it will never get.
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{Escape(entityId)}">
              <SPSSODescriptor AuthnRequestsSigned="false" WantAssertionsSigned="{(provider.RequireSignedAssertions ? "true" : "false")}"
                               protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <NameIDFormat>urn:oasis:names:tc:SAML:2.0:nameid-format:emailAddress</NameIDFormat>
                <AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"
                                          Location="{Escape(acs)}" index="0" isDefault="true"/>
              </SPSSODescriptor>
            </EntityDescriptor>
            """;
    }

    // --- account resolution -----------------------------------------------------------------

    /// <summary>
    /// Turns a validated federated identity into a NetRisk account, provisioning one when the provider
    /// allows it.
    ///
    /// Matching is by login first and email second. Not by the IdP subject: NetRisk has no column for
    /// it on <c>user</c>, and adding one would mean every existing account had to be re-linked before
    /// SSO worked at all.
    /// </summary>
    private async Task<FederatedSignInResult> ResolveAccountAsync(IdentityProvider provider,
        FederatedIdentity identity)
    {
        var login = identity.Login ?? identity.Email ?? identity.Subject;

        if (string.IsNullOrWhiteSpace(login))
            return FederatedSignInResult.Fail(
                "The identity provider returned no login, email or subject to identify the user by. "
                + "Check the claim mapping.", identity);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Login == login)
                   ?? (identity.Email == null
                       ? null
                       : await db.Users.FirstOrDefaultAsync(u => u.Email == identity.Email));

        var groupMapping = ParseGroupMapping(provider.GroupMappingJson);
        var matched = identity.Groups
            .Select(g => groupMapping.TryGetValue(g, out var target) ? target : null)
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();

        if (user == null)
        {
            if (!provider.JitProvisioning)
                return FederatedSignInResult.Fail(
                    $"'{login}' authenticated successfully but has no NetRisk account, and just-in-time "
                    + "provisioning is disabled for this provider.", identity);

            var roleId = matched.Select(m => m.Role).FirstOrDefault(r => r != null) is { } roleName
                ? (await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName))?.Value
                : null;

            user = new User
            {
                Login = login,
                Name = identity.Name ?? login,
                Email = identity.Email ?? login,
                // The federated account has no local password. A random one is stored rather than an
                // empty hash so that a basic-auth attempt against the account cannot succeed by
                // matching the empty string.
                Password = RandomNumberGenerator.GetBytes(32),
                Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                Type = "saml",
                Enabled = true,
                Lockout = 0,
                RoleId = roleId ?? provider.DefaultRoleId ?? 0,
                Admin = matched.Any(m => m.Admin),
                Lang = "en",
                LastPasswordChangeDate = DateTime.UtcNow,
                MultiFactor = 0,
                ChangePassword = 0
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            await AssignEntitiesAsync(db, user, provider, matched);

            Logger.Information("Provisioned user {Login} through identity provider {Provider}",
                login, provider.Name);

            return FederatedSignInResult.Ok(user.Value, user.Login, identity, provisioned: true,
                requiresSecondFactor: await RequiresHardwareFactorAsync(db, user));
        }

        if (user.Enabled != true || user.Lockout == 1)
            return FederatedSignInResult.Fail(
                $"'{login}' authenticated with the identity provider, but the NetRisk account is disabled.",
                identity);

        // Group mapping is reapplied on every sign-in, which is what makes removing someone from an
        // IdP group actually take away their NetRisk role rather than only stopping new grants.
        if (matched.Count > 0)
        {
            var roleName = matched.Select(m => m.Role).FirstOrDefault(r => r != null);

            if (roleName != null)
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                if (role != null) user.RoleId = role.Value;
            }

            user.Admin = matched.Any(m => m.Admin);

            await AssignEntitiesAsync(db, user, provider, matched);
        }

        user.LastLogin = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return FederatedSignInResult.Ok(user.Value, user.Login, identity,
            requiresSecondFactor: await RequiresHardwareFactorAsync(db, user));
    }

    /// <summary>
    /// Applies entity assignments from the group mapping (or the provider default), skipping ones the
    /// user already has so a re-login does not accumulate duplicate rows.
    /// </summary>
    private static async Task AssignEntitiesAsync(AuditableContext db, User user, IdentityProvider provider,
        List<GroupMappingTarget> matched)
    {
        var entityIds = matched.Where(m => m.EntityId != null).Select(m => m.EntityId!.Value).ToList();

        if (entityIds.Count == 0 && provider.DefaultEntityId != null)
            entityIds.Add(provider.DefaultEntityId.Value);

        if (entityIds.Count == 0) return;

        var existing = await db.UserEntityRoles
            .Where(uer => uer.UserId == user.Value && uer.RevokedAt == null)
            .Select(uer => uer.EntityId)
            .ToListAsync();

        // A per-entity role is required by the table, so a user with no global role cannot be given an
        // entity assignment through group mapping — the provider's default role has to cover it.
        if (user.RoleId == 0) return;

        foreach (var entityId in entityIds.Distinct().Except(existing))
        {
            if (!await db.Entities.AnyAsync(e => e.Id == entityId)) continue;

            db.UserEntityRoles.Add(new UserEntityRole
            {
                UserId = user.Value,
                EntityId = entityId,
                RoleId = user.RoleId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Whether this account is subject to the hardware-factor policy (4.3.3) and so must complete a
    /// WebAuthn ceremony before a session is issued.
    /// </summary>
    private async Task<bool> RequiresHardwareFactorAsync(AuditableContext db, User user)
    {
        var required = configuration["authentication:requireHardwareFactorForAdmins"];

        if (!bool.TryParse(required, out var enforce) || !enforce) return false;
        if (!user.Admin) return false;

        return await db.WebAuthnCredentials.AnyAsync(c => c.UserId == user.Value && c.RevokedAt == null);
    }

    // --- helpers ----------------------------------------------------------------------------

    internal record SamlMetadata(List<X509Certificate2> Certificates, string? SsoUrl, string? EntityId);

    /// <summary>
    /// Resolves SAML metadata from the pasted XML if there is one, otherwise by fetching the metadata
    /// URL. XML first because an air-gapped server cannot reach the URL and pasting is the documented
    /// fallback.
    /// </summary>
    private async Task<(SamlMetadata Metadata, string? Error)> ResolveSamlMetadataAsync(IdentityProvider provider)
    {
        var xml = provider.MetadataXml;

        if (string.IsNullOrWhiteSpace(xml))
        {
            if (string.IsNullOrWhiteSpace(provider.MetadataUrl))
                return (new SamlMetadata([], null, null),
                    "The provider has neither metadata XML nor a metadata URL configured.");

            var response = await http.SendAsync(new OutboundHttpRequest
            {
                Method = "GET",
                Url = provider.MetadataUrl
            });

            if (!response.IsSuccess)
                return (new SamlMetadata([], null, null),
                    response.StatusCode == 0
                        ? $"The metadata URL could not be reached: {response.TransportError}"
                        : $"The metadata URL answered HTTP {response.StatusCode}.");

            xml = response.Body;
        }

        if (string.IsNullOrWhiteSpace(xml))
            return (new SamlMetadata([], null, null), "The metadata document was empty.");

        var (certificates, ssoUrl, entityId, error) = SamlAssertion.ParseMetadata(xml);

        return (new SamlMetadata(certificates, ssoUrl, entityId), error);
    }

    /// <summary>
    /// Reads the OIDC discovery document and its key set.
    ///
    /// Fetched through <see cref="IOutboundHttpClient"/> rather than through
    /// <c>ConfigurationManager</c>'s own document retriever: that retriever opens its own socket, which
    /// would make every test of the sign-in flow reach a real host. Everything else — the document
    /// shape, the JWKS parsing, the token validation — is still the identity-model library's.
    /// </summary>
    internal async Task<OpenIdConnectConfiguration> GetDiscoveryAsync(IdentityProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Authority))
            throw new InvalidParameterException(nameof(provider.Authority),
                "An OIDC provider requires an authority.");

        var authority = provider.Authority.TrimEnd('/');

        lock (DiscoveryLock)
        {
            if (Discovery.TryGetValue(authority, out var cached)
                && DateTime.UtcNow - cached.FetchedAt < DiscoveryCacheLifetime)
                return cached.Configuration;
        }

        var document = await http.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = authority + "/.well-known/openid-configuration"
        });

        if (!document.IsSuccess)
            throw new IntegrationRequestException(provider.Name,
                document.StatusCode == 0
                    ? $"The identity provider's discovery document could not be fetched: {document.TransportError}"
                    : $"The identity provider answered HTTP {document.StatusCode} for its discovery document.");

        OpenIdConnectConfiguration configuration;

        try
        {
            configuration = OpenIdConnectConfiguration.Create(document.Body!);
        }
        catch (Exception ex)
        {
            throw new IntegrationRequestException(provider.Name,
                $"The identity provider's discovery document could not be read: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(configuration.JwksUri))
        {
            var jwks = await http.SendAsync(new OutboundHttpRequest
            {
                Method = "GET",
                Url = configuration.JwksUri
            });

            if (!jwks.IsSuccess)
                throw new IntegrationRequestException(provider.Name,
                    $"The identity provider's key set could not be fetched (HTTP {jwks.StatusCode}).");

            try
            {
                foreach (var key in new JsonWebKeySet(jwks.Body).GetSigningKeys())
                    configuration.SigningKeys.Add(key);
            }
            catch (Exception ex)
            {
                throw new IntegrationRequestException(provider.Name,
                    $"The identity provider's key set could not be read: {ex.Message}");
            }
        }

        lock (DiscoveryLock) Discovery[authority] = (configuration, DateTime.UtcNow);

        return configuration;
    }

    private static void InvalidateDiscovery(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority)) return;

        lock (DiscoveryLock) Discovery.Remove(authority.TrimEnd('/'));
    }

    /// <summary>Test seam: drops every cached discovery document so a test starts from nothing.</summary>
    internal static void ClearDiscoveryCache()
    {
        lock (DiscoveryLock) Discovery.Clear();
    }

    /// <summary>
    /// Only a loopback redirect (the desktop client's local listener, per RFC 8252) or an explicitly
    /// configured URI is accepted.
    ///
    /// Without this the endpoint is an open redirector that an attacker can point at their own host to
    /// collect authorization codes.
    /// </summary>
    internal bool IsAllowedRedirect(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) return false;

        if (uri.IsLoopback && uri.Scheme is "http" or "https") return true;

        var allowed = configuration["app:allowedRedirectUris"];

        return allowed != null && allowed
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => string.Equals(candidate, redirectUri, StringComparison.OrdinalIgnoreCase));
    }

    internal static ClaimMapping ParseClaimMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ClaimMapping();

        try
        {
            return JsonSerializer.Deserialize<ClaimMapping>(json, JsonOptions) ?? new ClaimMapping();
        }
        catch (JsonException)
        {
            // Defaults rather than a failure: a malformed mapping should produce a sign-in that reports
            // "no email claim" rather than a 500 nobody can interpret.
            return new ClaimMapping();
        }
    }

    internal static Dictionary<string, GroupMappingTarget> ParseGroupMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, GroupMappingTarget>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, GroupMappingTarget>>(json, JsonOptions);

            return parsed == null
                ? new Dictionary<string, GroupMappingTarget>(StringComparer.OrdinalIgnoreCase)
                // Case-insensitive: IdP group names differ in case between the directory and the token
                // more often than anyone expects.
                : new Dictionary<string, GroupMappingTarget>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, GroupMappingTarget>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string DefaultEntityId() =>
        (configuration["app:baseUrl"]?.TrimEnd('/') ?? "https://netrisk.local") + "/saml/metadata";

    private string DefaultAcsUrl(int providerId) =>
        (configuration["app:baseUrl"]?.TrimEnd('/') ?? "https://netrisk.local")
        + $"/IdentityProviders/{providerId}/saml/acs";

    private void Validate(IdentityProvider provider)
    {
        if (provider == null) throw new InvalidParameterException(nameof(provider), "A provider is required.");

        if (string.IsNullOrWhiteSpace(provider.Name))
            throw new InvalidParameterException(nameof(provider.Name), "A provider requires a name.");

        if (!Enum.IsDefined(provider.Protocol))
            throw new InvalidParameterException(nameof(provider.Protocol),
                "Protocol must be Oidc or Saml2.");

        if (provider.Protocol == IdentityProviderProtocol.Oidc)
        {
            if (string.IsNullOrWhiteSpace(provider.Authority)
                || !Uri.TryCreate(provider.Authority, UriKind.Absolute, out var authority)
                || (authority.Scheme != Uri.UriSchemeHttps && !authority.IsLoopback))
                throw new InvalidParameterException(nameof(provider.Authority),
                    "An OIDC authority must be an absolute https URL (http is allowed only for loopback).");

            if (string.IsNullOrWhiteSpace(provider.ClientId))
                throw new InvalidParameterException(nameof(provider.ClientId),
                    "An OIDC provider requires a client id.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(provider.MetadataUrl) && string.IsNullOrWhiteSpace(provider.MetadataXml))
                throw new InvalidParameterException(nameof(provider.MetadataUrl),
                    "A SAML provider requires either a metadata URL or metadata XML.");
        }

        if (provider.ClockSkewSeconds is < 0 or > 3600)
            throw new InvalidParameterException(nameof(provider.ClockSkewSeconds),
                "Clock skew tolerance must be between 0 and 3600 seconds.");
    }

    private static IdentityProvider Copy(IdentityProvider source, IdentityProvider target)
    {
        target.Name = source.Name.Trim();
        target.Protocol = source.Protocol;
        target.Enabled = source.Enabled;
        target.Authority = source.Authority?.TrimEnd('/');
        target.ClientId = source.ClientId;
        target.Scopes = source.Scopes;
        target.MetadataUrl = source.MetadataUrl;
        target.MetadataXml = source.MetadataXml;
        target.EntityIdValue = source.EntityIdValue;
        target.AssertionConsumerServiceUrl = source.AssertionConsumerServiceUrl;
        target.RequireSignedAssertions = source.RequireSignedAssertions;
        target.ClockSkewSeconds = source.ClockSkewSeconds;
        target.SupportsSingleLogout = source.SupportsSingleLogout;
        target.ClaimMappingJson = source.ClaimMappingJson;
        target.GroupMappingJson = source.GroupMappingJson;
        target.JitProvisioning = source.JitProvisioning;
        target.DefaultRoleId = source.DefaultRoleId;
        target.DefaultEntityId = source.DefaultEntityId;
        return target;
    }

    private static async Task<IdentityProvider> LoadAsync(AuditableContext db, int id) =>
        await db.IdentityProviders.FirstOrDefaultAsync(p => p.Id == id)
        ?? throw new DataNotFoundException("identity_providers", id.ToString(),
            new Exception($"Identity provider {id} was not found."));

    private static IdentityProviderView ToView(IdentityProvider provider) => new()
    {
        Id = provider.Id,
        Name = provider.Name,
        Protocol = provider.Protocol,
        Enabled = provider.Enabled,
        Authority = provider.Authority,
        ClientId = provider.ClientId,
        HasClientSecret = !string.IsNullOrEmpty(provider.EncryptedClientSecret),
        Scopes = provider.Scopes,
        MetadataUrl = provider.MetadataUrl,
        HasMetadataXml = !string.IsNullOrEmpty(provider.MetadataXml),
        EntityIdValue = provider.EntityIdValue,
        AssertionConsumerServiceUrl = provider.AssertionConsumerServiceUrl,
        RequireSignedAssertions = provider.RequireSignedAssertions,
        ClockSkewSeconds = provider.ClockSkewSeconds,
        SupportsSingleLogout = provider.SupportsSingleLogout,
        ClaimMapping = ParseClaimMapping(provider.ClaimMappingJson),
        GroupMapping = ParseGroupMapping(provider.GroupMappingJson),
        JitProvisioning = provider.JitProvisioning,
        DefaultRoleId = provider.DefaultRoleId,
        DefaultEntityId = provider.DefaultEntityId
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
