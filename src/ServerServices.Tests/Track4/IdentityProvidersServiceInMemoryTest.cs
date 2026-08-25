using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.IdentityModel.Tokens;
using Model.Authentication.Federation;
using Model.Exceptions;
using ServerServices.Auth;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Identity-provider configuration and the OIDC authorization-code flow
/// (Track 4 milestone 4.3.1).
///
/// The security-carrying properties: a redirect URI that is not a loopback address or explicitly
/// configured is refused, so this endpoint is not an open redirector for collecting authorization
/// codes; a state value is single-use, so an injected code cannot be replayed; an id_token minted for
/// another application at the same issuer is refused; and JIT provisioning is off unless a provider
/// asks for it, so an IdP that authenticates the whole company does not populate NetRisk with everyone
/// who clicked the wrong tile.
/// </summary>
[TestSubject(typeof(IdentityProvidersService))]
public class IdentityProvidersServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIdentityProvidersService _svc;

    private const string Authority = "https://login.acme.com";
    private const string ClientId = "netrisk-desktop";
    private const string Redirect = "http://127.0.0.1:51789/callback";

    private static readonly RsaSecurityKey SigningKey =
        new(RSA.Create(2048)) { KeyId = "test-key-1" };

    public IdentityProvidersServiceInMemoryTest()
    {
        IdentityProvidersService.ClearDiscoveryCache();

        _svc = GetService<IIdentityProvidersService>();

        Seed(ctx =>
        {
            ctx.Roles.Add(new Role { Value = 1, Name = "Analyst", Default = true, Admin = false });
            ctx.Roles.Add(new Role { Value = 2, Name = "Administrator", Default = false, Admin = true });

            ctx.Users.Add(new User
            {
                Value = 1, Name = "Alice", Login = "alice@acme.com", Email = "alice@acme.com",
                Enabled = true, Lockout = 0, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), RoleId = 1
            });
        });
    }

    private void StubDiscovery()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(SigningKey);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        FakeOutboundHttpClient
            .RuleFor("/.well-known/openid-configuration", $$"""
                {
                  "issuer": "{{Authority}}",
                  "authorization_endpoint": "{{Authority}}/authorize",
                  "token_endpoint": "{{Authority}}/token",
                  "jwks_uri": "{{Authority}}/keys"
                }
                """)
            .RuleFor("/keys", JsonSerializer.Serialize(new { keys = new[] { jwk } }));
    }

    private static string IdToken(string audience = ClientId, string issuer = Authority,
        string subject = "alice@acme.com", string email = "alice@acme.com", string name = "Alice Adams",
        string[]? groups = null, DateTime? expires = null)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("email", email),
            new("name", name)
        };

        foreach (var group in groups ?? []) claims.Add(new Claim("groups", group));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: (expires ?? DateTime.UtcNow.AddMinutes(10)).AddMinutes(-11),
            expires: expires ?? DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void StubTokenExchange(string idToken) =>
        FakeOutboundHttpClient.Rules.Insert(0, (
            request => request.Method == "POST" && request.Url.EndsWith("/token"),
            new OutboundHttpResponse
            {
                StatusCode = 200,
                Body = JsonSerializer.Serialize(new { id_token = idToken, token_type = "Bearer" })
            }));

    private Task<IdentityProviderView> OidcProviderAsync(bool jit = false, string? groupMapping = null,
        int? defaultRoleId = 1) =>
        _svc.CreateProviderAsync(new IdentityProvider
        {
            Name = "Acme Entra ID",
            Protocol = IdentityProviderProtocol.Oidc,
            Enabled = true,
            Authority = Authority,
            ClientId = ClientId,
            Scopes = "openid profile email",
            ClaimMappingJson = """{"email":"email","name":"name","subject":"sub","groups":"groups"}""",
            GroupMappingJson = groupMapping,
            JitProvisioning = jit,
            DefaultRoleId = defaultRoleId,
            ClockSkewSeconds = 120
        }, clientSecret: null);

    // --- configuration ----------------------------------------------------------------------

    [Fact]
    public async Task AProviderIsStoredWithItsSecretEncryptedAndNeverReturned()
    {
        var view = await _svc.CreateProviderAsync(new IdentityProvider
        {
            Name = "Acme", Protocol = IdentityProviderProtocol.Oidc, Enabled = true,
            Authority = Authority, ClientId = ClientId, ClockSkewSeconds = 120
        }, "top-secret");

        Assert.True(view.HasClientSecret);
        Assert.Null(typeof(IdentityProviderView).GetProperty("ClientSecret"));

        await using var db = OpenContext();
        Assert.NotEqual("top-secret", db.IdentityProviders.Single().EncryptedClientSecret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://login.acme.com")]
    public async Task AnOidcProviderNeedsAnHttpsAuthority(string? authority)
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateProviderAsync(new IdentityProvider
            {
                Name = "x", Protocol = IdentityProviderProtocol.Oidc, Authority = authority,
                ClientId = ClientId, ClockSkewSeconds = 120
            }, null));
    }

    [Fact]
    public async Task AnOidcProviderNeedsAClientId()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateProviderAsync(new IdentityProvider
            {
                Name = "x", Protocol = IdentityProviderProtocol.Oidc, Authority = Authority,
                ClockSkewSeconds = 120
            }, null));
    }

    [Fact]
    public async Task ASamlProviderNeedsMetadata()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateProviderAsync(new IdentityProvider
            {
                Name = "x", Protocol = IdentityProviderProtocol.Saml2, ClockSkewSeconds = 120
            }, null));
    }

    [Fact]
    public async Task TheSignInProviderListCarriesOnlyWhatAButtonNeeds()
    {
        await OidcProviderAsync();

        var available = await _svc.GetEnabledForSignInAsync();

        var provider = Assert.Single(available);

        Assert.Equal("Acme Entra ID", provider.Name);
        // Claim and group mappings are configuration, not something an anonymous caller enumerates.
        Assert.Null(provider.Authority);
        Assert.Empty(provider.GroupMapping);
    }

    [Fact]
    public async Task ADisabledProviderIsNotOfferedAtSignIn()
    {
        var view = await OidcProviderAsync();

        await _svc.UpdateProviderAsync(new IdentityProvider
        {
            Id = view.Id, Name = view.Name, Protocol = view.Protocol, Enabled = false,
            Authority = Authority, ClientId = ClientId, ClockSkewSeconds = 120
        }, null);

        Assert.Empty(await _svc.GetEnabledForSignInAsync());
    }

    [Fact]
    public async Task TestReadsTheDiscoveryDocumentAndReportsWhatItFound()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();

        var result = await _svc.TestProviderAsync(provider.Id);

        Assert.True(result.Success, result.Message);
        Assert.Equal(Authority, result.Details["Issuer"]);
        Assert.Equal("1", result.Details["Signing keys"]);
    }

    [Fact]
    public async Task TestReportsADiscoveryDocumentWithNoSigningKeys()
    {
        FakeOutboundHttpClient.RuleFor("/.well-known/openid-configuration", $$"""
            {"issuer":"{{Authority}}","authorization_endpoint":"{{Authority}}/authorize",
             "token_endpoint":"{{Authority}}/token"}
            """);

        var provider = await OidcProviderAsync();

        var result = await _svc.TestProviderAsync(provider.Id);

        // Without keys an id_token could not be validated, so this is a broken configuration rather
        // than a working one with a missing nicety.
        Assert.False(result.Success);
        Assert.Contains("signing keys", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- OIDC sign-in -----------------------------------------------------------------------

    [Fact]
    public async Task BeginningASignInProducesAPkceAuthorizationUrl()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();

        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        Assert.StartsWith($"{Authority}/authorize?", request.AuthorizationUrl);
        Assert.Contains("response_type=code", request.AuthorizationUrl);
        Assert.Contains("code_challenge_method=S256", request.AuthorizationUrl);
        Assert.Contains($"client_id={ClientId}", request.AuthorizationUrl);
        Assert.NotEmpty(request.State);

        // The verifier stays on the server, keyed by the state, so a client that cannot keep a secret
        // does not have to.
        Assert.DoesNotContain("code_verifier", request.AuthorizationUrl);
    }

    [Theory]
    [InlineData("https://attacker.example/collect")]
    [InlineData("not-a-url")]
    public async Task ARedirectUriThatIsNotLoopbackOrConfiguredIsRefused(string redirect)
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();

        // Without this the endpoint is an open redirector an attacker points at their own host to
        // collect authorization codes.
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.BeginOidcSignInAsync(provider.Id, redirect));
    }

    [Fact]
    public async Task ASamlProviderRefusesTheOidcSignInEndpoint()
    {
        var provider = await _svc.CreateProviderAsync(new IdentityProvider
        {
            Name = "SAML", Protocol = IdentityProviderProtocol.Saml2, Enabled = true,
            MetadataXml = "<x/>", ClockSkewSeconds = 120
        }, null);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.BeginOidcSignInAsync(provider.Id, Redirect));
    }

    [Fact]
    public async Task CompletingTheFlowResolvesAnExistingAccount()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken());

        var result = await _svc.CompleteOidcSignInAsync(request.State, "auth-code");

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.UserId);
        Assert.False(result.Provisioned);
        Assert.Equal("alice@acme.com", result.Identity!.Email);
    }

    [Fact]
    public async Task AStateIsSingleUse()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken());

        Assert.True((await _svc.CompleteOidcSignInAsync(request.State, "auth-code")).Success);

        // Replaying the same state with an injected code must fail; the entry is removed on redemption.
        var replay = await _svc.CompleteOidcSignInAsync(request.State, "auth-code");

        Assert.False(replay.Success);
        Assert.Contains("no longer valid", replay.Error!);
    }

    [Fact]
    public async Task AnUnknownStateIsRefused()
    {
        var result = await _svc.CompleteOidcSignInAsync("never-issued", "code");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AnIdTokenForAnotherApplicationIsRefused()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken(audience: "some-other-app"));

        var result = await _svc.CompleteOidcSignInAsync(request.State, "code");

        // The audience check is what stops a token minted for another application at the same issuer
        // from signing in here.
        Assert.False(result.Success);
        Assert.Contains("id_token was rejected", result.Error!);
    }

    [Fact]
    public async Task AnExpiredIdTokenIsRefused()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken(expires: DateTime.UtcNow.AddHours(-2)));

        Assert.False((await _svc.CompleteOidcSignInAsync(request.State, "code")).Success);
    }

    [Fact]
    public async Task AnIdTokenFromAnotherIssuerIsRefused()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken(issuer: "https://evil.example"));

        Assert.False((await _svc.CompleteOidcSignInAsync(request.State, "code")).Success);
    }

    [Fact]
    public async Task ARefusedAuthorizationCodeIsReported()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        FakeOutboundHttpClient.Rules.Insert(0, (
            r => r.Method == "POST" && r.Url.EndsWith("/token"),
            new OutboundHttpResponse { StatusCode = 400, Body = """{"error":"invalid_grant"}""" }));

        var result = await _svc.CompleteOidcSignInAsync(request.State, "code");

        Assert.False(result.Success);
        Assert.Contains("400", result.Error!);
    }

    [Fact]
    public async Task ATokenResponseWithNoIdTokenIsReportedWithTheLikelyCause()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        FakeOutboundHttpClient.Rules.Insert(0, (
            r => r.Method == "POST" && r.Url.EndsWith("/token"),
            new OutboundHttpResponse { StatusCode = 200, Body = """{"access_token":"a"}""" }));

        var result = await _svc.CompleteOidcSignInAsync(request.State, "code");

        Assert.False(result.Success);
        Assert.Contains("openid", result.Error!);
    }

    // --- account resolution -----------------------------------------------------------------

    [Fact]
    public async Task AnUnknownUserIsRefusedWhenJitProvisioningIsOff()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync(jit: false);
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken(subject: "bob@acme.com", email: "bob@acme.com"));

        var result = await _svc.CompleteOidcSignInAsync(request.State, "code");

        // Off by default: an IdP that authenticates the whole company would otherwise populate NetRisk
        // with everyone who clicked the wrong tile.
        Assert.False(result.Success);
        Assert.Contains("just-in-time provisioning is disabled", result.Error!);
    }

    [Fact]
    public async Task JitProvisioningCreatesTheAccountWhenTheProviderAsksForIt()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync(jit: true);
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken(subject: "bob@acme.com", email: "bob@acme.com", name: "Bob Brown"));

        var result = await _svc.CompleteOidcSignInAsync(request.State, "code");

        Assert.True(result.Success, result.Error);
        Assert.True(result.Provisioned);

        await using var db = OpenContext();
        var created = db.Users.Single(u => u.Login == "bob@acme.com");

        Assert.Equal("Bob Brown", created.Name);
        Assert.True(created.Enabled);
        // A federated account has no local password; random bytes rather than an empty hash so a
        // basic-auth attempt cannot match the empty string.
        Assert.NotEmpty(created.Password);
        Assert.Equal(1, created.RoleId);
    }

    [Fact]
    public async Task GroupMappingAssignsARoleAndTheAdministratorFlag()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync(jit: true,
            groupMapping: """{"Security-Admins":{"role":"Administrator","admin":true}}""");

        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken(subject: "carol@acme.com", email: "carol@acme.com",
            groups: ["Security-Admins", "Everyone"]));

        Assert.True((await _svc.CompleteOidcSignInAsync(request.State, "code")).Success);

        await using var db = OpenContext();
        var created = db.Users.Single(u => u.Login == "carol@acme.com");

        Assert.Equal(2, created.RoleId);
        Assert.True(created.Admin);
    }

    [Fact]
    public async Task GroupMappingIsReappliedOnEverySignIn()
    {
        StubDiscovery();

        var provider = await OidcProviderAsync(
            groupMapping: """{"Security-Admins":{"role":"Administrator","admin":true}}""");

        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);
        StubTokenExchange(IdToken(groups: ["Security-Admins"]));

        Assert.True((await _svc.CompleteOidcSignInAsync(request.State, "code")).Success);

        await using (var db = OpenContext())
            Assert.True(db.Users.Single(u => u.Value == 1).Admin);

        // Removed from the group at the IdP. Reapplying on every sign-in is what makes that actually
        // take the role away rather than only stopping new grants.
        FakeOutboundHttpClient.Rules.Clear();
        StubDiscovery();

        var second = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);
        StubTokenExchange(IdToken(groups: ["Everyone"]));

        Assert.True((await _svc.CompleteOidcSignInAsync(second.State, "code")).Success);

        await using (var db = OpenContext())
        {
            // No mapped group matched, so the account keeps whatever it had — the mapping only speaks
            // when it matches, and silently stripping every role on an unmapped login would lock people
            // out of a correctly configured tenant.
            Assert.True(db.Users.Single(u => u.Value == 1).Admin);
        }
    }

    [Fact]
    public async Task GroupMappingIsMatchedCaseInsensitively()
    {
        var mapping = IdentityProvidersService.ParseGroupMapping(
            """{"Security-Admins":{"role":"Administrator"}}""");

        // IdP group names differ in case between the directory and the token more often than anyone
        // expects.
        Assert.True(mapping.ContainsKey("security-admins"));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ADisabledNetRiskAccountCannotSignInEvenWithAValidToken()
    {
        StubDiscovery();

        Seed(ctx =>
        {
            var user = ctx.Users.Single(u => u.Value == 1);
            user.Enabled = false;
        });

        var provider = await OidcProviderAsync();
        var request = await _svc.BeginOidcSignInAsync(provider.Id, Redirect);

        StubTokenExchange(IdToken());

        var result = await _svc.CompleteOidcSignInAsync(request.State, "code");

        Assert.False(result.Success);
        Assert.Contains("disabled", result.Error!);
    }

    [Fact]
    public void AMalformedClaimMappingFallsBackToTheDefaults()
    {
        var mapping = IdentityProvidersService.ParseClaimMapping("{not json");

        // Defaults rather than a failure: a broken mapping should produce a sign-in that says "no email
        // claim" rather than a 500 nobody can interpret.
        Assert.Equal("email", mapping.Email);
        Assert.Equal("sub", mapping.Subject);
    }

    // --- SAML sign-in and metadata ----------------------------------------------------------

    [Fact]
    public async Task TheServiceProviderMetadataCarriesTheEntityIdAndAcsUrl()
    {
        var provider = await _svc.CreateProviderAsync(new IdentityProvider
        {
            Name = "SAML", Protocol = IdentityProviderProtocol.Saml2, Enabled = true,
            MetadataXml = "<x/>", EntityIdValue = "https://netrisk.acme.com/saml/metadata",
            AssertionConsumerServiceUrl = "https://netrisk.acme.com/acs",
            RequireSignedAssertions = true, ClockSkewSeconds = 120
        }, null);

        var metadata = await _svc.GetServiceProviderMetadataAsync(provider.Id);

        Assert.Contains("entityID=\"https://netrisk.acme.com/saml/metadata\"", metadata);
        Assert.Contains("Location=\"https://netrisk.acme.com/acs\"", metadata);
        Assert.Contains("WantAssertionsSigned=\"true\"", metadata);
    }

    [Fact]
    public async Task ASamlResponseWithNoRelayStateAndSeveralProvidersIsRefused()
    {
        await _svc.CreateProviderAsync(new IdentityProvider
        {
            Name = "SAML A", Protocol = IdentityProviderProtocol.Saml2, Enabled = true,
            MetadataXml = "<x/>", ClockSkewSeconds = 120
        }, null);

        await _svc.CreateProviderAsync(new IdentityProvider
        {
            Name = "SAML B", Protocol = IdentityProviderProtocol.Saml2, Enabled = true,
            MetadataXml = "<x/>", ClockSkewSeconds = 120
        }, null);

        var result = await _svc.CompleteSamlSignInAsync(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("<x/>")), relayState: null);

        // With two providers there is no way to know whose certificate should verify it, and trying them
        // all would let a compromised IdP impersonate users of the other.
        Assert.False(result.Success);
        Assert.Contains("more than one SAML provider", result.Error!);
    }

    [Fact]
    public async Task AnEmptySamlResponseIsRefused()
    {
        Assert.False((await _svc.CompleteSamlSignInAsync("", null)).Success);
    }

    [Fact]
    public async Task AnUnknownProviderIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetProviderAsync(404));
    }

    [Fact]
    public async Task DeletingAProviderRemovesIt()
    {
        var provider = await OidcProviderAsync();

        await _svc.DeleteProviderAsync(provider.Id);

        Assert.Empty(await _svc.GetProvidersAsync());
    }
}
