using Contracts;
using Contracts.Secrets;
using Xunit;

namespace BastionVaultPlugin.Tests;

/// <summary>
/// The BastionVault plugin against a stubbed vault.
///
/// Two things these tests are here to protect, beyond the obvious happy paths. First, the request
/// shape: the API key must be a bearer token and the machine ID must appear only when the connection
/// has one, because a machine ID sent as an empty header is a 401 that looks like a bad key. Second,
/// the failure behaviour the SDK contract specifies: <c>TestConnectionAsync</c> reports a bad
/// credential as a value, everything else throws <see cref="SecretVaultException"/>, and no message
/// anywhere contains the API key.
/// </summary>
public class BastionVaultSecretPluginTest
{
    private const string BaseUrl = "https://vault.example.com";
    private const string ApiKey = "bv-key-super-secret";

    private const string ListUrl = BaseUrl + "/api/v1/secrets";

    private static readonly BastionVaultSecretPlugin Plugin = new();

    private static SecretVaultContext Context(FakePluginHttpClient http, string? machineId = null) => new()
    {
        Credentials = new SecretVaultCredentials { BaseUrl = BaseUrl, ApiKey = ApiKey, MachineId = machineId },
        Http = http
    };

    private const string ListBody = """
        {
          "secrets": [
            { "id": "db-prod", "name": "Production database", "path": "infra/db",
              "fields": ["username", "password"], "version": "7",
              "updatedAt": "2026-08-01T10:00:00Z" },
            { "id": "tm-key", "name": "Vision One API key", "description": "EDR" },
            { "id": "", "name": "unusable — no id" }
          ]
        }
        """;

    // --- capability declaration --------------------------------------------------------------

    [Fact]
    public void DeclaresItsCapabilityAndIdentity()
    {
        Assert.IsAssignableFrom<INetriskPlugin>(Plugin);
        Assert.IsAssignableFrom<INetriskSecretVaultPlugin>(Plugin);

        Assert.Equal("BastionVaultPlugin", Plugin.PluginName);
        Assert.Equal("bastionvault", Plugin.VaultKind);

        // The machine ID is optional; TestConnectionAsync is what tells an operator when their
        // account nevertheless needs one.
        Assert.False(Plugin.RequiresMachineId);
    }

    [Fact]
    public void InitializeAndDisposeTolerateANullLogger()
    {
        var plugin = new BastionVaultSecretPlugin();

        plugin.Initialize(null);
        plugin.Dispose();
    }

    // --- request shape -------------------------------------------------------------------------

    [Fact]
    public async Task SendsTheApiKeyAsABearerTokenAndNoMachineIdWhenThereIsNone()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, ListBody);

        await Plugin.ListSecretsAsync(Context(http));

        var request = Assert.Single(http.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("Bearer " + ApiKey, request.Headers["Authorization"]);

        // Not merely empty — absent. A machine-binding header with no value is a request the vault
        // refuses, and the refusal reads as an authentication failure.
        Assert.False(request.Headers.ContainsKey("X-BastionVault-Machine-Id"));
    }

    [Fact]
    public async Task SendsTheMachineIdWhenTheConnectionCarriesOne()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, ListBody);

        await Plugin.ListSecretsAsync(Context(http, "machine-42"));

        Assert.Equal("machine-42", Assert.Single(http.Requests).Headers["X-BastionVault-Machine-Id"]);
    }

    [Fact]
    public async Task EscapesASecretIdIntoThePathRatherThanSplittingIt()
    {
        // A BastionVault id may be a path. Interpolating it raw would turn one secret into a
        // different URL — and, for an id containing "..", into a request for something else entirely.
        const string id = "infra/db prod";
        var url = BaseUrl + "/api/v1/secrets/" + Uri.EscapeDataString(id);

        var http = new FakePluginHttpClient().Respond(url, 200, """{"value":"s3cret"}""");

        var value = await Plugin.GetSecretAsync(Context(http), new VaultSecretReference { SecretId = id });

        Assert.Equal("s3cret", value.Value);
        Assert.Equal(url, Assert.Single(http.Requests).Url);
    }

    [Fact]
    public async Task StripsATrailingSlashFromTheBaseUrl()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, ListBody);

        var context = new SecretVaultContext
        {
            Credentials = new SecretVaultCredentials { BaseUrl = BaseUrl + "/", ApiKey = ApiKey },
            Http = http
        };

        await Plugin.ListSecretsAsync(context);

        Assert.Equal(ListUrl, Assert.Single(http.Requests).Url);
    }

    // --- listing -------------------------------------------------------------------------------

    [Fact]
    public async Task ListsSecretsWithTheirFieldsAndDropsOnesWithNoId()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, ListBody);

        var secrets = await Plugin.ListSecretsAsync(Context(http));

        Assert.Equal(2, secrets.Count);

        var db = secrets.Single(s => s.Id == "db-prod");
        Assert.Equal("Production database", db.Name);
        Assert.Equal("infra/db", db.Path);
        Assert.Equal(["username", "password"], db.Fields);
        Assert.Equal("7", db.Version);
        Assert.Equal(new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), db.UpdatedAt!.Value.ToUniversalTime());

        // No name in the payload: the id stands in, so the picker never shows a blank row.
        var tm = secrets.Single(s => s.Id == "tm-key");
        Assert.Equal("Vision One API key", tm.Name);
        Assert.Null(tm.Path);
        Assert.Empty(tm.Fields);
    }

    [Fact]
    public async Task AcceptsABareArrayAsWellAsTheDocumentedEnvelope()
    {
        var http = new FakePluginHttpClient()
            .Respond(ListUrl, 200, """[ { "id": "a", "name": "A" } ]""");

        var secrets = await Plugin.ListSecretsAsync(Context(http));

        Assert.Equal("a", Assert.Single(secrets).Id);
    }

    [Fact]
    public async Task ListingThrowsWhenTheVaultRefuses()
    {
        var http = new FakePluginHttpClient()
            .Respond(ListUrl, 403, """{"error":"key is not authorized for this scope"}""");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.ListSecretsAsync(Context(http)));

        Assert.Contains("403", ex.Message);
        Assert.Contains("not authorized", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message);
    }

    [Fact]
    public async Task ListingThrowsWhenTheResponseIsNotTheExpectedShape()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, "<html>proxy error</html>");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.ListSecretsAsync(Context(http)));

        Assert.Contains("could not be read", ex.Message);
    }

    // --- reading -------------------------------------------------------------------------------

    [Fact]
    public async Task ReadsASingleValueSecret()
    {
        var http = new FakePluginHttpClient()
            .Respond(BaseUrl + "/api/v1/secrets/tm-key", 200,
                """{"id":"tm-key","version":"3","value":"vision-one-key"}""");

        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "tm-key" });

        Assert.Equal("vision-one-key", value.Value);
        Assert.Equal("3", value.Version);
        Assert.Null(value.MaxCacheAge);
    }

    [Fact]
    public async Task ReadsANamedFieldOfAStructuredSecretCaseInsensitively()
    {
        var http = new FakePluginHttpClient()
            .Respond(BaseUrl + "/api/v1/secrets/db-prod", 200,
                """{"id":"db-prod","fields":{"username":"svc","password":"p4ss"}}""");

        // "Password" as the picker displayed it; "password" as the vault stores it. Not an error.
        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "db-prod", Field = "Password" });

        Assert.Equal("p4ss", value.Value);
    }

    [Fact]
    public async Task ReportsTheVaultsOwnCacheCap()
    {
        var http = new FakePluginHttpClient()
            .Respond(BaseUrl + "/api/v1/secrets/short", 200,
                """{"value":"x","maxCacheSeconds":30}""");

        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "short" });

        Assert.Equal(TimeSpan.FromSeconds(30), value.MaxCacheAge);
    }

    [Fact]
    public async Task ReadsTheOnlyFieldWhenNoFieldWasAskedForAndThereIsNoSingleValue()
    {
        // A vault administrator converting a plain secret into a one-field one must not break every
        // reference to it: with exactly one field there is nothing to be ambiguous about.
        var http = new FakePluginHttpClient()
            .Respond(BaseUrl + "/api/v1/secrets/solo", 200, """{"fields":{"token":"t0k"}}""");

        var value = await Plugin.GetSecretAsync(Context(http),
            new VaultSecretReference { SecretId = "solo" });

        Assert.Equal("t0k", value.Value);
    }

    [Fact]
    public async Task RefusesToGuessBetweenSeveralFields()
    {
        var http = new FakePluginHttpClient()
            .Respond(BaseUrl + "/api/v1/secrets/db-prod", 200,
                """{"fields":{"username":"svc","password":"p4ss"}}""");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http), new VaultSecretReference { SecretId = "db-prod" }));

        Assert.Contains("username", ex.Message);
        Assert.Contains("password", ex.Message);
    }

    [Fact]
    public async Task RefusesToFallBackWhenTheRequestedFieldIsGone()
    {
        // The regression this exists for: falling back to the secret's single value would hand a
        // username to something that asked for a password, and nothing would report an error until a
        // third party rejected the credential.
        var http = new FakePluginHttpClient()
            .Respond(BaseUrl + "/api/v1/secrets/db-prod", 200,
                """{"value":"svc","fields":{"username":"svc"}}""");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http),
                new VaultSecretReference { SecretId = "db-prod", Field = "password" }));

        Assert.Contains("no field 'password'", ex.Message);
        Assert.Contains("username", ex.Message);
    }

    [Fact]
    public async Task ReadingAMissingSecretSaysSo()
    {
        var http = new FakePluginHttpClient().Respond(BaseUrl + "/api/v1/secrets/gone", 404, "");

        var ex = await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http), new VaultSecretReference { SecretId = "gone" }));

        Assert.Contains("no such secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadingRejectsAnEmptySecretId()
    {
        var http = new FakePluginHttpClient();

        await Assert.ThrowsAsync<SecretVaultException>(
            () => Plugin.GetSecretAsync(Context(http), new VaultSecretReference { SecretId = "  " }));

        Assert.Empty(http.Requests);
    }

    // --- connection test -------------------------------------------------------------------------

    [Fact]
    public async Task TestReportsHowManySecretsTheKeyCanSee()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, ListBody);

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.True(result.Success);
        Assert.Equal(2, result.VisibleSecretCount);
        Assert.Contains("2 secret", result.Message);
    }

    [Fact]
    public async Task TestSucceedsButWarnsWhenTheKeyCanSeeNothing()
    {
        // Reachable and authenticated, but useless. Reporting this as a success with no comment is
        // how an administrator concludes the integration is configured and moves on.
        var http = new FakePluginHttpClient().Respond(ListUrl, 200, """{"secrets":[]}""");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.True(result.Success);
        Assert.Equal(0, result.VisibleSecretCount);
        Assert.Contains("no secrets", result.Message);
    }

    [Fact]
    public async Task TestReportsABadCredentialAsAValueRatherThanThrowing()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 401, """{"message":"invalid api key"}""");

        var result = await Plugin.TestConnectionAsync(Context(http, "machine-42"));

        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
        Assert.DoesNotContain(ApiKey, result.Message);
    }

    [Fact]
    public async Task TestPointsAtTheMissingMachineIdWhenTheVaultRefusesAndThereIsNone()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 401, "");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.Contains("machine ID", result.Message);
    }

    [Fact]
    public async Task TestDoesNotBlameTheMachineIdWhenOneIsSet()
    {
        var http = new FakePluginHttpClient().Respond(ListUrl, 401, "");

        var result = await Plugin.TestConnectionAsync(Context(http, "machine-42"));

        Assert.False(result.Success);
        Assert.DoesNotContain("No machine ID is set", result.Message);
    }

    [Fact]
    public async Task TestReportsAnUnreachableVaultDistinctlyFromARefusedOne()
    {
        var http = new FakePluginHttpClient().RespondUnreachable(ListUrl, "Name or service not known");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.Contains("could not be reached", result.Message);
        Assert.Contains("Name or service not known", result.Message);
    }

    [Fact]
    public async Task AnErrorBodyThatIsNotJsonIsReducedToItsStatusCode()
    {
        // A proxy's HTML error page may contain anything, including a reflected credential. Only a
        // parsed error/message field is quoted back to the operator.
        var http = new FakePluginHttpClient()
            .Respond(ListUrl, 500, "<html><body>upstream said " + ApiKey + "</body></html>");

        var result = await Plugin.TestConnectionAsync(Context(http));

        Assert.False(result.Success);
        Assert.DoesNotContain(ApiKey, result.Message);
        Assert.Contains("500", result.Message);
    }
}
