using Contracts;
using Contracts.Secrets;
using Xunit;

namespace BastionVaultPlugin.Tests;

/// <summary>
/// The BastionVault plugin against a stubbed vault.
///
/// These tests were rewritten once, and the reason is worth recording: the first implementation was
/// written against a plausible-looking REST API that BastionVault does not have. Everything below
/// therefore pins the protocol as the server source actually implements it, and several assertions
/// exist specifically to stop a regression back to the guess —
/// <see cref="AuthenticatesWithTheVaultTokenHeaderAndNotABearerToken"/> and
/// <see cref="ListingUsesTheListVerbAndNotAQueryParameter"/> most of all. The second is the sharpest:
/// <c>GET …?list=true</c> is offered by BastionVault's own documentation but the logical router maps
/// GET to a Read unconditionally, so that request would <em>read the secret</em> rather than list
/// under it.
/// </summary>
public class BastionVaultSecretPluginTest
{
    private const string BaseUrl = "https://vault.example.com:8200";
    private const string Token = "s.bv-token-super-secret";

    private const string MountsUrl = BaseUrl + "/v1/sys/mounts";
    private const string LookupUrl = BaseUrl + "/v1/auth/token/lookup-self";
    private const string RequirementUrl = BaseUrl + "/v1/auth/ferrogate/requirement";
    private const string SecretRootUrl = BaseUrl + "/v1/secret/";

    private static readonly BastionVaultSecretPlugin Plugin = new();

    private static SecretVaultContext Context(FakePluginHttpClient http, string? machineId = null) => new()
    {
        Credentials = new SecretVaultCredentials { BaseUrl = BaseUrl, ApiKey = Token, MachineId = machineId },
        Http = http
    };

    private const string MountsBody = """
        { "data": {
            "secret/":    { "type": "kv",        "description": "key/value" },
            "cubbyhole/": { "type": "cubbyhole", "description": "per-token" },
            "pki/":       { "type": "pki",       "description": "certificates" }
        } }
        """;

    private const string LookupBody = """
        { "data": { "id": "s.x", "policies": ["default", "netrisk"], "display_name": "netrisk",
                    "meta": {} } }
        """;

    /// <summary>A vault with one folder and two leaves, wired for the whole walk.</summary>
    private static FakePluginHttpClient Vault()
    {
        return new FakePluginHttpClient()
            .Respond(LookupUrl, 200, LookupBody)
            .Respond(RequirementUrl, 404, "")
            .Respond(MountsUrl, 200, MountsBody)
            .Respond(SecretRootUrl, 200, """{ "data": { "keys": ["tm-key", "prod/"] } }""")
            .Respond(BaseUrl + "/v1/secret/prod/", 200, """{ "data": { "keys": ["db"] } }""");
    }

    // --- capability declaration --------------------------------------------------------------

    [Fact]
    public void DeclaresItsCapabilityAndIdentity()
    {
        Assert.IsAssignableFrom<INetriskPlugin>(Plugin);
        Assert.IsAssignableFrom<INetriskSecretVaultPlugin>(Plugin);

        Assert.Equal("BastionVaultPlugin", Plugin.PluginName);
        Assert.Equal("bastionvault", Plugin.VaultKind);

        // Machine identity is a server-side policy, discovered at test time — not a field NetRisk can
        // decide is mandatory.
        Assert.False(Plugin.RequiresMachineId);
    }

    [Fact]
    public void InitializeAndDisposeTolerateANullLogger()
    {
        var plugin = new BastionVaultSecretPlugin();

        plugin.Initialize(null);
        plugin.Dispose();
    }

    // --- protocol -------------------------------------------------------------------------------

    [Fact]
    public async Task AuthenticatesWithTheVaultTokenHeaderAndNotABearerToken()
    {
        var http = Vault();

        await Plugin.ListSecretsAsync(Context(http));

        Assert.All(http.Requests, request =>
        {
            Assert.Equal(Token, request.Headers["X-Vault-Token"]);

            // A bearer header is not merely redundant here: BastionVault ignores it, so the request
            // arrives unauthenticated and the failure reads as "no token" rather than "wrong token".
            Assert.False(request.Headers.ContainsKey("Authorization"));
        });
    }

    [Fact]
    public async Task ListingUsesTheListVerbAndNotAQueryParameter()
    {
        var http = Vault();

        await Plugin.ListSecretsAsync(Context(http));

        var listings = http.Requests.Where(r => r.Method == "LIST").ToArray();

        Assert.NotEmpty(listings);
        Assert.Contains(listings, r => r.Url == SecretRootUrl);

        // The regression this guards: BastionVault's logical router maps GET to Operation::Read
        // unconditionally and lifts only `env` and `version` out of the query string, so
        // `GET secret/?list=true` reads the secret at `secret/` instead of listing under it.
        Assert.DoesNotContain(http.Requests, r => r.Url.Contains("list=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutsEveryRouteUnderTheV1Prefix()
    {
        var http = Vault();

        await Plugin.ListSecretsAsync(Context(http));

        Assert.All(http.Requests, r => Assert.StartsWith(BaseUrl + "/v1/", r.Url));
    }

    [Fact]
    public async Task StripsATrailingSlashFromTheBaseUrl()
    {
        var http = Vault();

        var context = new SecretVaultContext
        {
            Credentials = new SecretVaultCredentials { BaseUrl = BaseUrl + "/", ApiKey = Token },
            Http = http
        };

        await Plugin.ListSecretsAsync(context);

        Assert.Contains(http.Requests, r => r.Url == MountsUrl);
    }

    // --- listing -------------------------------------------------------------------------------

    [Fact]
    public async Task WalksTheTreeAndReturnsFullLogicalPaths()
    {
        var secrets = await Plugin.ListSecretsAsync(Context(Vault()));

        // A BastionVault listing is one level deep, so "prod/" had to be descended into.
        Assert.Equal(2, secrets.Count);

        var top = secrets.Single(s => s.Id == "secret/tm-key");
        Assert.Equal("tm-key", top.Name);
        Assert.Equal("secret", top.Path);

        var nested = secrets.Single(s => s.Id == "secret/prod/db");
        Assert.Equal("db", nested.Name);
        Assert.Equal("secret/prod", nested.Path);
    }

    [Fact]
    public async Task ReportsNoFieldNamesBecauseAListingDoesNotRevealThem()
    {
        var secrets = await Plugin.ListSecretsAsync(Context(Vault()));

        // Learning a secret's field names means reading the secret, which would put an access record
        // in the vault's audit log for every click in the picker. The field is typed instead, and
        // GetSecretAsync names the real fields when a wrong one is used.
        Assert.All(secrets, s => Assert.Empty(s.Fields));
    }

    [Fact]
    public async Task ListsOnlySecretsEngineMounts()
    {
        var http = Vault();

        await Plugin.ListSecretsAsync(Context(http));

        // pki holds certificates, not referencable secrets; cubbyhole is per-token storage that would
        // vanish with the token that listed it, so a reference into it could never resolve again.
        Assert.DoesNotContain(http.Requests, r => r.Url.Contains("/v1/pki/", StringComparison.Ordinal));
        Assert.DoesNotContain(http.Requests, r => r.Url.Contains("cubbyhole", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkipsAFolderTheTokenMayNotListRatherThanFailing()
    {
        var http = Vault()
            .Respond(SecretRootUrl, 200, """{ "data": { "keys": ["ok", "forbidden/"] } }""")
            .Respond(BaseUrl + "/v1/secret/forbidden/", 403, """{"errors":["permission denied"]}""");

        var secrets = await Plugin.ListSecretsAsync(Context(http));

        // A token scoped to the paths NetRisk needs is a good configuration, and it will be denied on
        // its siblings. Failing the whole enumeration would punish exactly the careful operator.
        Assert.Equal("secret/ok", Assert.Single(secrets).Id);
    }

    [Fact]
    public async Task TreatsAnEmptyFolderAsEmptyRatherThanAnError()
    {
        var http = Vault().Respond(BaseUrl + "/v1/secret/prod/", 404, "");

        var secrets = await Plugin.ListSecretsAsync(Context(http));

        Assert.Equal("secret/tm-key", Assert.Single(secrets).Id);
    }

    [Fact]
    public async Task FallsBackToTheConventionalMountWhenTheMountTableIsDenied()
    {
        // Reading sys/mounts is a privilege a careful operator will not grant NetRisk. The common
        // configuration — a token scoped to one KV path — must keep working without it.
        var http = Vault().Respond(MountsUrl, 403, """{"errors":["permission denied"]}""");

        var secrets = await Plugin.ListSecretsAsync(Context(http));

        Assert.Equal(2, secrets.Count);
    }

    [Fact]
    public async Task ListingThrowsWhenTheVaultIsSealed()
    {
        var http = Vault().Respond(MountsUrl, 503, """{"errors":["Vault is sealed"]}""");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.ListSecretsAsync(Context(http)));

        // The one failure whose remedy has nothing to do with NetRisk's configuration.
        Assert.Contains("sealed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListingThrowsWhenTheResponseIsNotTheExpectedShape()
    {
        var http = Vault().Respond(MountsUrl, 200, "<html>proxy error</html>");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.ListSecretsAsync(Context(http)));

        Assert.Contains("could not be read", ex.Message);
    }

    // --- reading -------------------------------------------------------------------------------

    [Fact]
    public async Task ReadsANamedFieldCaseInsensitively()
    {
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/v1/secret/prod/db", 200,
            """{ "lease_duration": 3600, "data": { "username": "svc", "password": "p4ss" } }""");

        // "Password" as an operator typed it; "password" as the vault stores it. Not an error.
        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "secret/prod/db", Field = "Password" });

        Assert.Equal("p4ss", value.Value);
        Assert.Equal(TimeSpan.FromHours(1), value.MaxCacheAge);
    }

    [Fact]
    public async Task ReadsTheOnlyFieldWhenNoFieldWasNamed()
    {
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/v1/secret/tm-key", 200,
            """{ "data": { "value": "vision-one-key" } }""");

        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "secret/tm-key" });

        Assert.Equal("vision-one-key", value.Value);
        Assert.Null(value.MaxCacheAge);
    }

    [Fact]
    public async Task RendersANonStringFieldAsItsRawJson()
    {
        // A credential stored as a number is still the credential the caller asked for, and refusing
        // it would be a surprise the operator cannot act on from NetRisk.
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/v1/secret/port", 200,
            """{ "data": { "port": 5432 } }""");

        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "secret/port", Field = "port" });

        Assert.Equal("5432", value.Value);
    }

    [Fact]
    public async Task RefusesToGuessBetweenSeveralFields()
    {
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/v1/secret/prod/db", 200,
            """{ "data": { "username": "svc", "password": "p4ss" } }""");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http),
                new VaultSecretReference { SecretId = "secret/prod/db" }));

        Assert.Contains("username", ex.Message);
        Assert.Contains("password", ex.Message);
    }

    [Fact]
    public async Task RefusesToFallBackWhenTheRequestedFieldIsGone()
    {
        // The regression this exists for: returning some other field would hand a username to
        // something that asked for a password, and nothing would report it until a third party
        // rejected the credential.
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/v1/secret/prod/db", 200,
            """{ "data": { "username": "svc" } }""");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http),
                new VaultSecretReference { SecretId = "secret/prod/db", Field = "password" }));

        Assert.Contains("no field 'password'", ex.Message);
        Assert.Contains("username", ex.Message);
    }

    [Fact]
    public async Task ReadingAMissingSecretSaysSo()
    {
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/v1/secret/gone", 404, "");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http),
                new VaultSecretReference { SecretId = "secret/gone" }));

        Assert.Contains("nothing at that path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("/")]
    [InlineData("secret/../sys/mounts")]
    [InlineData("secret/./db")]
    public async Task RejectsASecretPathThatIsNotUsable(string secretId)
    {
        // A reference is a path, so traversal is a real concern: '..' would reach a different mount
        // entirely, and a bare '/' would list rather than read.
        var http = new FakePluginHttpClient();

        await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http), new VaultSecretReference { SecretId = secretId }));

        Assert.Empty(http.Requests);
    }

    // --- connection test -------------------------------------------------------------------------

    [Fact]
    public async Task TestIntrospectsTheTokenAndCountsWhatItCanSee()
    {
        var http = Vault();

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.True(result.Success);
        Assert.Equal(2, result.VisibleSecretCount);
        Assert.Contains("2 secret", result.Message);
        Assert.Contains("netrisk", result.Message);   // the token's policies
        Assert.Contains(http.Requests, r => r.Url == LookupUrl);
    }

    [Fact]
    public async Task TestSucceedsButWarnsWhenTheTokenCanSeeNothing()
    {
        // Reachable and authenticated, but useless. Reporting this as a plain success is how an
        // administrator concludes the integration is configured and moves on.
        var http = Vault().Respond(SecretRootUrl, 200, """{ "data": { "keys": [] } }""");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.True(result.Success);
        Assert.Equal(0, result.VisibleSecretCount);
        Assert.Contains("no secrets", result.Message);
    }

    [Fact]
    public async Task TestReportsABadTokenAsAValueRatherThanThrowing()
    {
        var http = Vault().Respond(LookupUrl, 403, """{"errors":["permission denied"]}""");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.Contains("403", result.Message);
        Assert.DoesNotContain(Token, result.Message);
    }

    [Fact]
    public async Task TestReportsASealedVaultDistinctly()
    {
        var http = Vault().Respond(LookupUrl, 503, """{"errors":["Vault is sealed"]}""");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.Contains("sealed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestReportsAnUnreachableVaultDistinctlyFromARefusedOne()
    {
        var http = Vault().RespondUnreachable(LookupUrl, "Name or service not known");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.Contains("could not be reached", result.Message);
    }

    // --- machine identity --------------------------------------------------------------------------

    [Fact]
    public async Task TestPassesWhenTheServerDoesNotRequireMachineIdentity()
    {
        // A 404 on the requirement endpoint is the ordinary answer on a server with no FerroGate auth
        // method mounted, and must not be treated as a refusal.
        var result = await Plugin.TestConnectionAsync(Context(Vault()));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task TestRefusesANonMachineBoundTokenWhenTheServerRequiresOne()
    {
        var http = Vault().Respond(RequirementUrl, 200,
            """
            { "data": { "require_machine_identity": true, "mia_environment": "hml",
                        "expected_audience": "https://vault.example.com" } }
            """);

        var result = await Plugin.TestConnectionAsync(Context(http));

        // The server would refuse every subsequent request, so the test has to say so — and name the
        // command that produces a usable token.
        Assert.False(result.Success);
        Assert.Contains("requires machine identity", result.Message);
        Assert.Contains("bvault ferrogate token", result.Message);
        Assert.Contains("hml", result.Message);
    }

    [Fact]
    public async Task TestAcceptsAMachineBoundTokenWhenTheServerRequiresOne()
    {
        var http = Vault()
            .Respond(LookupUrl, 200,
                """
                { "data": { "policies": ["default"],
                            "meta": { "spiffe_id": "spiffe://ferrogate.prod/host/abc" } } }
                """)
            .Respond(RequirementUrl, 200, """{ "data": { "require_machine_identity": true } }""");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.True(result.Success);
        Assert.Contains("spiffe://ferrogate.prod/host/abc", result.Message);
    }

    [Fact]
    public async Task TestRefusesATokenBoundToADifferentMachineThanTheConnectionDeclares()
    {
        var http = Vault().Respond(LookupUrl, 200,
            """
            { "data": { "policies": ["default"],
                        "meta": { "spiffe_id": "spiffe://ferrogate.prod/host/other" } } }
            """);

        var result = await Plugin.TestConnectionAsync(
            Context(http, machineId: "spiffe://ferrogate.prod/host/netrisk"));

        // A machine-bound token is a specific host's credential. Accepting one issued for a different
        // machine would make the connection's machine ID decorative.
        Assert.False(result.Success);
        Assert.Contains("host/netrisk", result.Message);
        Assert.Contains("host/other", result.Message);
    }

    [Fact]
    public async Task TestAcceptsTheDeclaredMachineWhenItMatches()
    {
        var http = Vault().Respond(LookupUrl, 200,
            """
            { "data": { "policies": ["default"],
                        "meta": { "spiffe_id": "spiffe://ferrogate.prod/host/netrisk" } } }
            """);

        var result = await Plugin.TestConnectionAsync(
            Context(http, machineId: "spiffe://ferrogate.prod/host/netrisk"));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task AnUnreadableRequirementEndpointDoesNotFailTheTest()
    {
        // Advisory only. A connection test must not fail because an optional endpoint was unreachable.
        var http = Vault().RespondUnreachable(RequirementUrl, "connection reset");

        Assert.True((await Plugin.TestConnectionAsync(Context(http))).Success);
    }

    [Fact]
    public async Task AnErrorBodyThatIsNotTheVaultErrorShapeIsReducedToItsStatusCode()
    {
        // A proxy's HTML page may contain anything, including a reflected credential. Only a parsed
        // `errors` array is quoted back to the operator.
        var http = Vault().Respond(LookupUrl, 500, "<html>upstream said " + Token + "</html>");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.DoesNotContain(Token, result.Message);
        Assert.Contains("500", result.Message);
    }
}
