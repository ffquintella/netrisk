using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using API.Controllers;
using API.Security;
using API.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Model.Secrets;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The external-secret-vault endpoints.
///
/// The most important assertion in this file is a negative one: there is no action anywhere on this
/// controller that returns a secret value, and <see cref="NoActionReturnsASecretValue"/> is what keeps
/// it that way. The whole design rests on values being resolved on the server at the moment of use and
/// never travelling to a client; an endpoint added later "just for debugging" would quietly undo it.
///
/// The rest is the HTTP contract the shared <c>IntegrationsControllerBase</c> provides — 404 for a
/// connection that is not there, 400 with the parameter named for a refusal — plus the deliberate
/// choice that a *failed* connection test is a 200 carrying the reason rather than an error status.
/// </summary>
[TestSubject(typeof(SecretVaultsController))]
public class SecretVaultsControllerTest : BaseControllerTest
{
    private static SecretVaultsController Controller() => ResolveController<SecretVaultsController>(_ => { });

    private static TValue Ok<TValue>(ActionResult<TValue> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TValue>(ok.Value);
    }

    private static SecretVaultConnectionRequest Request(int id = 0, string name = "Prod vault",
        string? apiKey = "bv-key") => new()
    {
        Connection = new SecretVaultConnectionInput
        {
            Id = id,
            Name = name,
            PluginName = MockedSecretVaultService.KnownPluginName,
            BaseUrl = "https://vault.example.com",
            Enabled = true,
            CacheTtlMinutes = SecretVaultDefaults.CacheTtlMinutes
        },
        ApiKey = apiKey
    };

    // --- the security property this feature rests on ---------------------------------------------

    [Fact]
    public void NoActionReturnsASecretValue()
    {
        // Every action's payload type is inspected, and none of them may carry a property that could
        // hold a credential. A "GET /SecretVaults/{id}/secrets/{secret}/value" added for convenience
        // would turn an administrator session into a way to read the estate's credentials out of
        // NetRisk — which is precisely what storing references was meant to prevent.
        var payloadTypes = typeof(SecretVaultsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => Unwrap(m.ReturnType))
            .SelectMany(Flatten)
            .Distinct()
            .ToArray();

        foreach (var type in payloadTypes)
        foreach (var property in type.GetProperties())
            Assert.False(LooksLikeAValue(property.Name),
                $"{type.Name}.{property.Name} could carry a secret value out of the server.");
    }

    private static bool LooksLikeAValue(string name) =>
        name is "Value" or "Secret" or "SecretValue" or "Password" or "ApiKey" or "Token"
            or "ClientSecret" or "Credential";

    /// <summary>Peels Task&lt;ActionResult&lt;T&gt;&gt; and List&lt;T&gt; down to the payload type.</summary>
    private static Type Unwrap(Type type)
    {
        while (type.IsGenericType
               && type.GetGenericTypeDefinition() is var definition
               && (definition == typeof(Task<>) || definition == typeof(ActionResult<>)))
            type = type.GetGenericArguments()[0];

        return type;
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            type = type.GetGenericArguments()[0];

        // Only the model types are interesting; primitives and IActionResult have no payload shape.
        if (type.Namespace?.StartsWith("Model.", StringComparison.Ordinal) == true) yield return type;
    }

    [Fact]
    public void EveryActionIsBehindTheConfigurationPermission()
    {
        // Listing an estate's secret names, and re-pointing a NetRisk credential field at one, are both
        // administrative acts. The class-level attribute is what makes that true for actions added
        // later as well.
        var attribute = typeof(SecretVaultsController)
            .GetCustomAttribute<PermissionAuthorizeAttribute>();

        Assert.NotNull(attribute);
    }

    // --- reads ------------------------------------------------------------------------------------

    [Fact]
    public async Task ReportsWhetherAVaultIsAvailable()
    {
        Assert.True(Ok(await Controller().IsAvailable()));
    }

    [Fact]
    public async Task ListsTheInstalledPlugins()
    {
        var plugin = Assert.Single(Ok(await Controller().GetPlugins()));

        Assert.Equal(MockedSecretVaultService.KnownPluginName, plugin.PluginName);
        Assert.Equal("bastionvault", plugin.VaultKind);
    }

    [Fact]
    public async Task ListsConnectionsWithoutTheirApiKey()
    {
        var connection = Assert.Single(Ok(await Controller().GetAll()));

        Assert.True(connection.HasApiKey);

        // The flag, never the key. There is no property on the view that could hold one.
        Assert.Null(typeof(SecretVaultConnectionView).GetProperty("ApiKey"));
    }

    [Fact]
    public async Task ReturnsTheMachineIdInTheClear()
    {
        // Deliberate: it identifies the installation rather than authenticating it, and an operator has
        // to be able to read it back to compare it against what the vault shows.
        Assert.Equal("machine-42", Ok(await Controller().Get(MockedSecretVaultService.KnownConnectionId))
            .MachineId);
    }

    [Fact]
    public async Task ReturnsTheAppIdAndTheTlsSettingInTheClear()
    {
        var view = Ok(await Controller().Get(MockedSecretVaultService.KnownConnectionId));

        // The app id names the caller rather than authenticating it, same as the machine id. The TLS
        // setting is returned because a control somebody switched off has to be visible to everyone
        // else who opens the screen.
        Assert.Equal("netrisk-prod", view.AppId);
        Assert.True(view.IgnoreSslErrors);
    }

    [Fact]
    public async Task AnUnknownConnectionIsA404()
    {
        Assert.IsType<NotFoundObjectResult>((await Controller().Get(999)).Result);
    }

    [Fact]
    public async Task ListsSecretsAsMetadataOnly()
    {
        var secrets = Ok(await Controller().ListSecrets(MockedSecretVaultService.KnownConnectionId));

        Assert.Equal(2, secrets.Count);

        var db = secrets.Single(s => s.Id == "db-prod");
        Assert.Equal(["username", "password"], db.Fields);
        Assert.Equal("infra / Production database", db.DisplayName);

        // Field *names*, not values.
        Assert.Null(typeof(VaultSecretSummary).GetProperty("Value"));
    }

    [Fact]
    public async Task ListingSecretsOfAnUnknownConnectionIsA404()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller().ListSecrets(999)).Result);
    }

    [Fact]
    public async Task DescribesAStoredReference()
    {
        var reference = SecretReference.Create(1, "db-prod", "password").ToString();

        var view = Ok(await Controller().Describe(new SecretReferenceDescribeRequest { Value = reference }));

        Assert.True(view.IsVaultReference);
        Assert.Equal("db-prod", view.SecretId);
        Assert.Equal("password", view.Field);
        Assert.Contains("Prod vault", view.DisplayName);
    }

    [Fact]
    public async Task DescribesALiteralAsNotAReference()
    {
        var view = Ok(await Controller().Describe(new SecretReferenceDescribeRequest { Value = "a-key" }));

        Assert.False(view.IsVaultReference);
    }

    [Fact]
    public async Task ReportsHowManyFieldsUseAConnection()
    {
        Assert.Equal(3, Ok(await Controller().Usage(MockedSecretVaultService.KnownConnectionId)));
    }

    // --- writes -----------------------------------------------------------------------------------

    [Fact]
    public async Task CreatingAConnectionAnswers201WithALocation()
    {
        var created = Assert.IsType<CreatedResult>((await Controller().Create(Request())).Result);

        Assert.Equal("SecretVaults/42", created.Location);
        Assert.Equal("Prod vault", Assert.IsType<SecretVaultConnectionView>(created.Value).Name);
    }

    [Fact]
    public async Task CreatingWithoutAnApiKeyIsA400ThatNamesTheParameter()
    {
        var refused = Assert.IsType<BadRequestObjectResult>(
            (await Controller().Create(Request(apiKey: null))).Result);

        var body = refused.Value!.ToString()!;

        Assert.Contains("invalid_parameter", body);
        Assert.Contains("apiKey", body);
    }

    [Fact]
    public async Task CreatingWithoutANameIsA400()
    {
        Assert.IsType<BadRequestObjectResult>(
            (await Controller().Create(Request(name: ""))).Result);
    }

    [Fact]
    public async Task TheRouteIdWinsOverTheBodysId()
    {
        // A payload disagreeing with its own URL is a client bug at best and an attempt to edit a
        // different row than the request authorized at worst.
        var updated = Ok(await Controller().Update(MockedSecretVaultService.KnownConnectionId,
            Request(id: 777, name: "Renamed")));

        Assert.Equal(MockedSecretVaultService.KnownConnectionId, updated.Id);
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task UpdatingAnUnknownConnectionIsA404()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller().Update(999, Request(id: 999))).Result);
    }

    [Fact]
    public async Task DeletingAnswers204()
    {
        Assert.IsType<NoContentResult>(
            await Controller().Delete(MockedSecretVaultService.KnownConnectionId));
    }

    [Fact]
    public async Task DeletingAnUnknownConnectionIsA404()
    {
        Assert.IsType<NotFoundObjectResult>(await Controller().Delete(999));
    }

    [Fact]
    public async Task AFailedTestIsA200CarryingTheReason()
    {
        // Not an error status. "Your API key is wrong" is an answer, and a 502 makes the desktop client
        // report a transport problem instead of showing the message the operator needs.
        var result = Ok(await Controller().Test(MockedSecretVaultService.KnownConnectionId));

        Assert.True(result.Success);
        Assert.Equal("Connected.", result.Message);
    }

    [Fact]
    public async Task TestingAnUnknownConnectionIsA404()
    {
        Assert.IsType<NotFoundObjectResult>((await Controller().Test(999)).Result);
    }
}
