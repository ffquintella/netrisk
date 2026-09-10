using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Secrets;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// The desktop client's half of the secret-vault feature, over <see cref="StubRestBackend"/> so every
/// URL it builds and every status branch runs for real.
///
/// The property this file exists to hold: the client can list secrets and bind a field to one, and has
/// no way whatsoever to read a value. There is no method for it and no type with somewhere to put one
/// — which is what makes a stolen desktop session much less interesting than the credentials it would
/// otherwise have handed over.
/// </summary>
[TestSubject(typeof(IntegrationsRestService))]
public class SecretVaultRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IIntegrationsService _service;

    public SecretVaultRestServiceTest()
    {
        _service = ResolveWith<IIntegrationsService>(_backend);
    }

    [Fact]
    public async Task AsksWhetherAVaultIsAvailable()
    {
        _backend.OnGet("/SecretVaults/available", true);

        Assert.True(await _service.IsSecretVaultAvailableAsync());
        Assert.True(_backend.Sent(Method.Get, "/SecretVaults/available"));
    }

    [Fact]
    public async Task AnUnreachableServerIsNotReportedAsNoVault()
    {
        // Falling back to false here would silently hide every picker button whenever the server hiccups,
        // which reads to an operator as "the feature disappeared".
        _backend.OnTransportFailure(Method.Get, "/SecretVaults/available");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.IsSecretVaultAvailableAsync());
    }

    [Fact]
    public async Task ListsThePlugins()
    {
        _backend.OnGet("/SecretVaults/plugins", new List<SecretVaultPluginInfo>
        {
            new() { PluginName = "BastionVaultPlugin", VaultKind = "bastionvault", RequiresMachineId = true }
        });

        var plugin = Assert.Single(await _service.GetSecretVaultPluginsAsync());

        Assert.Equal("BastionVaultPlugin", plugin.PluginName);
        Assert.True(plugin.RequiresMachineId);
    }

    [Fact]
    public async Task ListsConnectionsAndPassesTheIncludeDisabledFlag()
    {
        _backend.OnGet("/SecretVaults", new List<SecretVaultConnectionView>
        {
            new() { Id = 1, Name = "Prod vault", HasApiKey = true, PluginAvailable = true }
        });

        var connection = Assert.Single(await _service.GetSecretVaultConnectionsAsync(includeDisabled: false));

        Assert.Equal("Prod vault", connection.Name);
        Assert.Contains("includeDisabled=false", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task CreatesAConnectionWithTheApiKeyInItsOwnField()
    {
        _backend.OnPost("/SecretVaults", new SecretVaultConnectionView { Id = 7, Name = "Prod vault" },
            HttpStatusCode.Created);

        var input = new SecretVaultConnectionInput
        {
            Name = "Prod vault", PluginName = "BastionVaultPlugin",
            BaseUrl = "https://vault.example.com"
        };

        var created = await _service.CreateSecretVaultConnectionAsync(input, "bv-key");

        Assert.Equal(7, created.Id);

        // The key rides in its own property, not on the connection object, which is what lets an
        // update send null for "unchanged".
        Assert.Contains("\"apiKey\":\"bv-key\"", _backend.LastRequest.Body);
    }

    /// <summary>
    /// The app id and the TLS option are part of the connection, not of the key, so they must survive
    /// the serialization the request goes through. A field the client silently drops is a setting an
    /// operator ticks and that never reaches the server.
    /// </summary>
    [Fact]
    public async Task SendsTheAppIdAndTheTlsSettingWithTheConnection()
    {
        _backend.OnPost("/SecretVaults", new SecretVaultConnectionView { Id = 7, Name = "Prod vault" },
            HttpStatusCode.Created);

        var input = new SecretVaultConnectionInput
        {
            Name = "Prod vault", PluginName = "BastionVaultPlugin",
            BaseUrl = "https://vault.example.com",
            AppId = "netrisk-prod",
            IgnoreSslErrors = true
        };

        await _service.CreateSecretVaultConnectionAsync(input, "bv-key");

        Assert.Contains("\"appId\":\"netrisk-prod\"", _backend.LastRequest.Body);
        Assert.Contains("\"ignoreSslErrors\":true", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task UpdatesWithoutAnApiKeyWhenTheOperatorDidNotTypeOne()
    {
        _backend.OnPut("/SecretVaults/7", new SecretVaultConnectionView { Id = 7, Name = "Renamed" });

        await _service.UpdateSecretVaultConnectionAsync(
            new SecretVaultConnectionInput { Id = 7, Name = "Renamed" }, null);

        Assert.Contains("\"apiKey\":null", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task DeletesAConnection()
    {
        _backend.OnStatus(Method.Delete, "/SecretVaults/7", HttpStatusCode.NoContent);

        await _service.DeleteSecretVaultConnectionAsync(7);

        Assert.True(_backend.Sent(Method.Delete, "/SecretVaults/7"));
    }

    [Fact]
    public async Task ARefusedDeleteSurfacesTheServersReason()
    {
        // The interesting refusal: fields still resolve through this connection, and the count is in
        // the body. Losing it would leave the operator with "the request failed".
        _backend.On(Method.Delete, "/SecretVaults/7",
            "3 credential field(s) still resolve through the vault connection 'Prod vault'.",
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.DeleteSecretVaultConnectionAsync(7));

        Assert.Contains("3 credential field", ex.Message);
    }

    [Fact]
    public async Task TestsAConnection()
    {
        _backend.OnPost("/SecretVaults/7/test",
            new SecretVaultTestResultView { Success = true, Message = "Connected.", VisibleSecretCount = 4 });

        var result = await _service.TestSecretVaultConnectionAsync(7);

        Assert.True(result.Success);
        Assert.Equal(4, result.VisibleSecretCount);
    }

    [Fact]
    public async Task ListsSecretsAsMetadata()
    {
        _backend.OnGet("/SecretVaults/7/secrets", new List<VaultSecretSummary>
        {
            new() { Id = "db-prod", Name = "Production database", Path = "infra",
                    Fields = ["username", "password"] }
        });

        var secret = Assert.Single(await _service.GetVaultSecretsAsync(7));

        Assert.Equal("infra / Production database", secret.DisplayName);
        Assert.Equal(["username", "password"], secret.Fields);
    }

    [Fact]
    public void HasNoWayToReadASecretValue()
    {
        // The negative assertion the whole design rests on. A convenience method added later would put
        // the estate's credentials one desktop session away.
        foreach (var method in typeof(IIntegrationsService).GetMethods())
            Assert.DoesNotContain("SecretValue", method.Name);

        Assert.Null(typeof(VaultSecretSummary).GetProperty("Value"));
        Assert.Null(typeof(SecretVaultConnectionView).GetProperty("ApiKey"));
    }

    [Fact]
    public async Task DescribesAStoredReferenceThroughAPostRatherThanAQueryString()
    {
        var reference = SecretReference.Create(3, "db-prod", "password").ToString();

        _backend.OnPost("/SecretVaults/describe", new SecretReferenceView
        {
            IsVaultReference = true, Resolvable = true, ConnectionId = 3, SecretId = "db-prod",
            Field = "password", DisplayName = "Prod vault: db-prod / password"
        });

        var view = await _service.DescribeSecretReferenceAsync(reference);

        Assert.True(view.IsVaultReference);
        Assert.Equal("Prod vault: db-prod / password", view.DisplayName);

        // A reference names a secret. Names in URLs end up in access logs, proxy logs and history.
        Assert.Equal("/SecretVaults/describe", _backend.LastRequest.Path);
        Assert.Empty(_backend.LastRequest.Query);
        Assert.Contains(reference, _backend.LastRequest.Body);
    }

    [Fact]
    public async Task ReportsHowManyFieldsUseAConnection()
    {
        _backend.OnGet("/SecretVaults/7/usage", 5);

        Assert.Equal(5, await _service.GetSecretVaultUsageAsync(7));
    }

    [Fact]
    public async Task AnEmptyAnswerToAListIsAnEmptyListRatherThanNull()
    {
        _backend.OnStatus(Method.Get, "/SecretVaults", HttpStatusCode.NoContent);

        Assert.Empty(await _service.GetSecretVaultConnectionsAsync());
    }
}
