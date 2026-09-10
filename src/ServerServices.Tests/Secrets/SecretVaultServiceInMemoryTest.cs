using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Secrets;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Secrets;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Secrets;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// Vault connections and the resolution of a stored reference into a live credential.
///
/// The behaviours worth protecting, and why each one is here:
///
///  * A resolution failure is an exception with the vault named, never an empty string. An empty
///    credential produces a 401 from a third party and an operator debugging the wrong integration.
///  * The cache is consulted, and it is invalidated when the connection changes. Serving a credential
///    fetched with a key that has since been rotated is the failure a 15-minute TTL exists to bound.
///  * Deleting a connection that fields still resolve through is refused. There is no foreign key to
///    stop it, so this check is the only thing that does.
///  * A missing or disabled plugin is reported in words an operator can act on, because "the plugin
///    is not installed" and "the API key is wrong" have entirely different remedies.
/// </summary>
[TestSubject(typeof(SecretVaultService))]
public class SecretVaultServiceInMemoryTest : InMemoryServiceTestBase
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private readonly FakeSecretVaultPlugin _plugin = new();
    private readonly IPluginsService _plugins = Substitute.For<IPluginsService>();
    private readonly ObfuscatedSecretCache _cache = new(Log);
    private readonly ISecretProtector _protector;
    private readonly FakeDnsSrvLookup _dns = new();
    private readonly IVaultEndpointResolver _endpoints;
    private readonly ISecretVaultService _svc;

    public SecretVaultServiceInMemoryTest()
    {
        _protector = GetService<ISecretProtector>();

        _plugin.With("db-prod", ("username", "svc"), ("password", "p4ss"))
               .With("tm-key", "vision-one-key");

        ArrangePluginInstalled(enabled: true);

        // The real resolver, not a substitute: every connection in this class uses a direct https://
        // address, which the resolver returns without touching DNS or the network. That keeps the
        // node-selection path in the same code these tests exercise, so a change that broke a direct
        // address would fail here rather than only in the discovery tests.
        _endpoints = new VaultEndpointResolver(Log, _dns, FakeOutboundHttpClient);

        _svc = new SecretVaultService(Log, GetService<IDalService>(), _protector, _plugins, _cache,
            new PluginHttpClientFactory(FakeOutboundHttpClient), _endpoints);
    }

    /// <summary>
    /// Wires the substituted plugin host. Split out because several tests need to change the answer —
    /// a plugin that is absent, or present but switched off, are different failures with different
    /// messages.
    /// </summary>
    private void ArrangePluginInstalled(bool enabled, bool installed = true)
    {
        _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>(Arg.Any<string>())
            .Returns(installed ? _plugin : null);

        _plugins.GetEnabledPluginsAsync<INetriskSecretVaultPlugin>()
            .Returns(installed && enabled
                ? new System.Collections.Generic.List<INetriskSecretVaultPlugin> { _plugin }
                : []);

        _plugins.PluginIsEnabledAsync(Arg.Any<string>()).Returns(enabled);
    }

    private SecretVaultConnectionInput Input(string name = "Prod vault") => new()
    {
        Name = name,
        PluginName = _plugin.PluginName,
        BaseUrl = "https://vault.example.com",
        Enabled = true,
        CacheTtlMinutes = SecretVaultDefaults.CacheTtlMinutes
    };

    private async Task<SecretVaultConnectionView> CreateAsync(string name = "Prod vault") =>
        await _svc.CreateConnectionAsync(Input(name), "bv-api-key");

    // --- connection management ------------------------------------------------------------------

    [Fact]
    public async Task CreatesAConnectionAndDoesNotStoreTheApiKeyInClear()
    {
        var created = await CreateAsync();

        Assert.True(created.HasApiKey);
        Assert.Equal("fake", created.VaultKind);
        Assert.True(created.PluginAvailable);

        var stored = Read(created.Id);
        Assert.NotNull(stored.EncryptedApiKey);
        Assert.DoesNotContain("bv-api-key", stored.EncryptedApiKey);
        Assert.Equal("bv-api-key", _protector.Unprotect(stored.EncryptedApiKey));
    }

    [Fact]
    public async Task RefusesAConnectionWithNoApiKey()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateConnectionAsync(Input(), null));

        Assert.Contains("API key", ex.Message);
    }

    [Theory]
    [InlineData("", "https://vault.example.com")]
    [InlineData("Prod", "")]
    // A bare DNS name is no longer malformed — it is the cluster form, discovered by SRV record, and
    // is covered by RefusesAnAddressThatIsNeitherAUrlNorADnsName below. A scheme-less host:port still
    // is: Uri reads it as a scheme named "vault.example.com".
    [InlineData("Prod", "vault.example.com:4200")]
    [InlineData("Prod", "ftp://vault.example.com")]
    [InlineData("Prod", "file:///etc/passwd")]
    public async Task RefusesAMalformedConnection(string name, string baseUrl)
    {
        var input = Input();
        input.Name = name;
        input.BaseUrl = baseUrl;

        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateConnectionAsync(input, "key"));
    }

    [Fact]
    public async Task RefusesADuplicateName()
    {
        await CreateAsync("Prod vault");

        await Assert.ThrowsAsync<InvalidParameterException>(() => CreateAsync("Prod vault"));
    }

    [Fact]
    public async Task ClampsANonsenseCacheTtlRatherThanRefusingIt()
    {
        var input = Input();
        input.CacheTtlMinutes = 9999;

        var created = await _svc.CreateConnectionAsync(input, "key");

        Assert.Equal(SecretVaultDefaults.MaxCacheTtlMinutes, created.CacheTtlMinutes);

        input = Input("Second");
        input.CacheTtlMinutes = 0;

        Assert.Equal(SecretVaultDefaults.MinCacheTtlMinutes,
            (await _svc.CreateConnectionAsync(input, "key")).CacheTtlMinutes);
    }

    [Fact]
    public async Task AnUpdateWithNoApiKeyKeepsTheStoredOne()
    {
        var created = await CreateAsync();

        var input = Input();
        input.Id = created.Id;
        input.Name = "Renamed";

        await _svc.UpdateConnectionAsync(input, null);

        // The convention every connection endpoint follows: a blank credential box means "unchanged",
        // so a form showing a redacted placeholder cannot overwrite a working key with the placeholder.
        Assert.Equal("bv-api-key", _protector.Unprotect(Read(created.Id).EncryptedApiKey));
        Assert.Equal("Renamed", Read(created.Id).Name);
    }

    [Fact]
    public async Task ReportsWhenTheNamedPluginIsNotInstalled()
    {
        var created = await CreateAsync();

        ArrangePluginInstalled(enabled: false, installed: false);

        var view = await _svc.GetConnectionAsync(created.Id);

        // The UI needs to say *why* nothing resolves. Without this an operator sees an enabled
        // connection that silently fails.
        Assert.False(view.PluginAvailable);
        Assert.Equal(string.Empty, view.VaultKind);
    }

    // --- testing and listing --------------------------------------------------------------------

    [Fact]
    public async Task TestRecordsItsOutcomeOnTheRow()
    {
        var created = await CreateAsync();

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.True(result.Success);
        Assert.Equal(2, result.VisibleSecretCount);

        var stored = Read(created.Id);
        Assert.True(stored.LastTestSucceeded);
        Assert.NotNull(stored.LastTestAt);
        Assert.Contains("fake vault", stored.LastTestMessage!);
    }

    [Fact]
    public async Task TestReportsAPluginThatThrewAsAFailureRatherThanPropagating()
    {
        var created = await CreateAsync();
        _plugin.ThrowUnexpected = new InvalidOperationException("plugin exploded");

        var result = await _svc.TestConnectionAsync(created.Id);

        // A plugin is third-party code and may throw anything. An administrator pressing Test gets a
        // message; the API does not get a 500.
        Assert.False(result.Success);
        Assert.Contains("plugin exploded", result.Message);
        Assert.False(Read(created.Id).LastTestSucceeded);
    }

    [Fact]
    public async Task TestReportsAMissingPluginInWordsThatSayWhatToDo()
    {
        var created = await CreateAsync();
        ArrangePluginInstalled(enabled: false, installed: false);

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.False(result.Success);
        Assert.Contains("not installed", result.Message);
        Assert.Contains("Plugins/" + SecretVaultDefaults.PluginDirectory, result.Message);
    }

    [Fact]
    public async Task TestReportsADisabledPluginDistinctlyFromAMissingOne()
    {
        var created = await CreateAsync();
        ArrangePluginInstalled(enabled: false);

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.False(result.Success);
        Assert.Contains("disabled", result.Message);
    }

    [Fact]
    public async Task TestReportsAMissingMachineIdWhenThePluginRequiresOne()
    {
        var created = await CreateAsync();
        _plugin.RequiresMachineId = true;

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.False(result.Success);
        Assert.Contains("machine", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _plugin.TestCalls);
    }

    [Fact]
    public async Task PassesTheMachineIdThroughWhenTheConnectionHasOne()
    {
        var input = Input();
        input.MachineId = "machine-42";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        await _svc.TestConnectionAsync(created.Id);

        Assert.Equal("machine-42", _plugin.LastCredentials!.MachineId);
        Assert.Equal("bv-api-key", _plugin.LastCredentials.ApiKey);
        Assert.Equal("https://vault.example.com", _plugin.LastCredentials.BaseUrl);
    }

    [Fact]
    public async Task AnAbsentMachineIdIsPassedAsNullRatherThanAnEmptyString()
    {
        // An empty machine-binding header is a request a machine-bound vault refuses, and the refusal
        // reads as a bad API key.
        var created = await CreateAsync();

        await _svc.TestConnectionAsync(created.Id);

        Assert.Null(_plugin.LastCredentials!.MachineId);
    }

    [Fact]
    public async Task TestReportsAMissingAppIdWhenThePluginRequiresOne()
    {
        var created = await CreateAsync();
        _plugin.RequiresAppId = true;

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.False(result.Success);
        Assert.Contains("app id", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _plugin.TestCalls);
    }

    [Fact]
    public async Task PassesTheAppIdThroughWhenTheConnectionHasOne()
    {
        var input = Input();
        input.AppId = "netrisk-prod";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        await _svc.TestConnectionAsync(created.Id);

        Assert.Equal("netrisk-prod", _plugin.LastCredentials!.AppId);
        Assert.Equal("netrisk-prod", created.AppId);
    }

    [Fact]
    public async Task AnAbsentAppIdIsPassedAsNullRatherThanAnEmptyString()
    {
        // Same trap as the machine ID: a vault that authorizes by application reads an empty app id
        // as an unknown application, and refuses in the words it uses for a bad key.
        var created = await CreateAsync();

        await _svc.TestConnectionAsync(created.Id);

        Assert.Null(_plugin.LastCredentials!.AppId);
    }

    // --- TLS validation -------------------------------------------------------------------------

    /// <summary>
    /// The option reaches the wire, and it reaches it through the seam the plugin is handed rather
    /// than through anything the plugin controls. Asserted on the outbound request because that is
    /// the only place the setting is observable — the plugin cannot see it, by design.
    /// </summary>
    [Fact]
    public async Task ValidatesTheVaultCertificateUnlessTheConnectionSaysNotTo()
    {
        _plugin.CallUrl = "https://vault.example.com/v1/health";

        var created = await CreateAsync();

        await _svc.TestConnectionAsync(created.Id);

        Assert.True(FakeOutboundHttpClient.Requests.Count > 0);
        Assert.All(FakeOutboundHttpClient.Requests, r => Assert.False(r.AllowInvalidCertificate));
    }

    [Fact]
    public async Task SkipsCertificateValidationWhenTheConnectionAsksFor()
    {
        _plugin.CallUrl = "https://vault.example.com/v1/health";

        var input = Input();
        input.IgnoreSslErrors = true;

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        Assert.True(created.IgnoreSslErrors);

        await _svc.TestConnectionAsync(created.Id);

        var request = FakeOutboundHttpClient.Requests[^1];
        Assert.Equal("https://vault.example.com/v1/health", request.Url);
        Assert.True(request.AllowInvalidCertificate);
    }

    /// <summary>
    /// Turning it off again must take effect on the next call, not on the next restart: an operator
    /// who installs the vault's CA and unticks the box has fixed the problem, and a seam built once
    /// and reused would keep sending unvalidated requests.
    /// </summary>
    [Fact]
    public async Task TurningCertificateValidationBackOnTakesEffectImmediately()
    {
        _plugin.CallUrl = "https://vault.example.com/v1/health";

        var input = Input();
        input.IgnoreSslErrors = true;

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");
        await _svc.TestConnectionAsync(created.Id);

        var update = Input();
        update.Id = created.Id;
        update.IgnoreSslErrors = false;

        await _svc.UpdateConnectionAsync(update, null);
        await _svc.TestConnectionAsync(created.Id);

        Assert.False(FakeOutboundHttpClient.Requests[^1].AllowInvalidCertificate);
    }

    // --- addresses: one node, or a cluster ------------------------------------------------------

    /// <summary>
    /// The address field takes a cluster name, and the plugin is handed the node discovery picked —
    /// never the cluster name. A plugin built against the SDK concatenates a path onto whatever it is
    /// given, so a cluster name reaching it produces a request to a URL with no scheme.
    /// </summary>
    [Fact]
    public async Task AClusterNameIsResolvedToANodeBeforeThePluginSeesIt()
    {
        _dns.With("_bvault._tcp.vault.example.com", "node2.example.com", 4200);

        var input = Input();
        input.BaseUrl = "vault.example.com";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.True(result.Success);
        Assert.Equal("https://node2.example.com:4200", _plugin.LastCredentials!.BaseUrl);

        // The connection keeps the cluster name it was given: rewriting it to the node that happened
        // to answer today would turn an HA connection into a single-node one on the first Test.
        Assert.Equal("vault.example.com", Read(created.Id).BaseUrl);
    }

    /// <summary>
    /// Which node answered reaches the operator, and is persisted on the row — "the cluster works"
    /// and "one node of three works" are different answers.
    /// </summary>
    [Fact]
    public async Task TestReportsTheNodeItReachedForAClusterAddress()
    {
        _dns.With("_bvault._tcp.vault.example.com", "node2.example.com", 4200);

        var input = Input();
        input.BaseUrl = "vault.example.com";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.Equal("https://node2.example.com:4200", result.ResolvedEndpoint);
        Assert.Contains("node2.example.com", Read(created.Id).LastTestMessage!);
    }

    /// <summary>A direct address reports no node, because it would only repeat what was typed.</summary>
    [Fact]
    public async Task TestReportsNoNodeForADirectAddress()
    {
        var created = await CreateAsync();

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.Equal(string.Empty, result.ResolvedEndpoint);
        Assert.Null(result.EndpointNote);
    }

    [Fact]
    public async Task TestReportsAClusterWithNoSrvRecordsAsAFailure()
    {
        var input = Input();
        input.BaseUrl = "nosuchcluster.example.com";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        var result = await _svc.TestConnectionAsync(created.Id);

        Assert.False(result.Success);
        Assert.Contains("_bvault._tcp.nosuchcluster.example.com", result.Message);
        Assert.Equal(0, _plugin.TestCalls);
    }

    [Theory]
    [InlineData("vault.example.com:4200")]
    [InlineData("ftp://vault.example.com")]
    [InlineData("srv+https://vault.example.com:4200")]
    public async Task RefusesAnAddressThatIsNeitherAUrlNorADnsName(string address)
    {
        var input = Input();
        input.BaseUrl = address;

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateConnectionAsync(input, "bv-api-key"));

        Assert.Equal(nameof(SecretVaultConnectionInput.BaseUrl), ex.ParameterName);
    }

    // --- machine identity, enforced at save time ------------------------------------------------

    /// <summary>
    /// A plugin that binds credentials to a machine and a connection saved without a machine ID is a
    /// configuration that can never resolve. Before this, the only check was in <c>OpenAsync</c> —
    /// which fires inside a background job, hours later, rather than on the form.
    /// </summary>
    [Fact]
    public async Task RefusesToCreateAConnectionWithNoMachineIdWhenThePluginRequiresOne()
    {
        _plugin.RequiresMachineId = true;

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateConnectionAsync(Input(), "bv-api-key"));

        Assert.Equal(nameof(SecretVaultConnectionInput.MachineId), ex.ParameterName);
        Assert.Contains(_plugin.PluginName, ex.Message);
    }

    [Fact]
    public async Task RefusesToClearTheMachineIdWhenThePluginRequiresOne()
    {
        var input = Input();
        input.MachineId = "machine-42";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        _plugin.RequiresMachineId = true;

        var update = Input();
        update.Id = created.Id;
        update.MachineId = "   ";

        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.UpdateConnectionAsync(update, null));

        // Refused, not partially applied: the stored machine ID is still there.
        Assert.Equal("machine-42", Read(created.Id).MachineId);
    }

    [Fact]
    public async Task AcceptsAConnectionWithAMachineIdWhenThePluginRequiresOne()
    {
        _plugin.RequiresMachineId = true;

        var input = Input();
        input.MachineId = "machine-42";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        Assert.True(created.RequiresMachineId);
        Assert.Equal("machine-42", created.MachineId);
    }

    /// <summary>
    /// The requirement is only enforced when the plugin is there to declare it. A connection prepared
    /// before its DLL is deployed must still be savable, or the only order that works is
    /// install-then-configure — and the view already reports the plugin as unavailable, so the state
    /// is visible rather than silent.
    /// </summary>
    [Fact]
    public async Task DoesNotEnforceAMachineIdWhenThePluginIsNotInstalled()
    {
        _plugin.RequiresMachineId = true;
        ArrangePluginInstalled(enabled: false, installed: false);

        var created = await _svc.CreateConnectionAsync(Input(), "bv-api-key");

        Assert.False(created.PluginAvailable);
        Assert.Null(created.MachineId);
    }

    [Fact]
    public async Task RefusesToCreateAConnectionWithNoAppIdWhenThePluginRequiresOne()
    {
        _plugin.RequiresAppId = true;

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateConnectionAsync(Input(), "bv-api-key"));

        Assert.Equal(nameof(SecretVaultConnectionInput.AppId), ex.ParameterName);
        Assert.Contains(_plugin.PluginName, ex.Message);
    }

    [Fact]
    public async Task AcceptsAConnectionWithAnAppIdWhenThePluginRequiresOne()
    {
        _plugin.RequiresAppId = true;

        var input = Input();
        input.AppId = "netrisk-prod";

        var created = await _svc.CreateConnectionAsync(input, "bv-api-key");

        Assert.True(created.RequiresAppId);
        Assert.Equal("netrisk-prod", created.AppId);
    }

    [Fact]
    public async Task RefusesAnAppIdLongerThanTheColumn()
    {
        var input = Input();
        input.AppId = new string('a', 256);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateConnectionAsync(input, "bv-api-key"));

        Assert.Equal(nameof(SecretVaultConnectionInput.AppId), ex.ParameterName);
    }

    [Fact]
    public async Task ListsSecretsWithTheirFields()
    {
        var created = await CreateAsync();

        var secrets = await _svc.ListSecretsAsync(created.Id);

        Assert.Equal(2, secrets.Count);
        Assert.Equal(["username", "password"], secrets.Single(s => s.Id == "db-prod").Fields);
        Assert.Empty(secrets.Single(s => s.Id == "tm-key").Fields);
    }

    [Fact]
    public async Task ListingSurfacesAVaultRefusalAsAnUpstreamFailure()
    {
        var created = await CreateAsync();
        _plugin.FailWith = "the key is not authorized";

        var ex = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => _svc.ListSecretsAsync(created.Id));

        Assert.Contains("not authorized", ex.Message);
    }

    // --- resolution -----------------------------------------------------------------------------

    [Fact]
    public async Task ResolvesASingleValueSecret()
    {
        var created = await CreateAsync();

        var value = await _svc.ResolveAsync(SecretReference.Create(created.Id, "tm-key"));

        Assert.Equal("vision-one-key", value);
    }

    [Fact]
    public async Task ResolvesAFieldOfAStructuredSecret()
    {
        var created = await CreateAsync();

        Assert.Equal("p4ss",
            await _svc.ResolveAsync(SecretReference.Create(created.Id, "db-prod", "password")));
        Assert.Equal("svc",
            await _svc.ResolveAsync(SecretReference.Create(created.Id, "db-prod", "username")));
    }

    [Fact]
    public async Task ServesASecondResolutionFromTheCache()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);
        await _svc.ResolveAsync(reference);
        await _svc.ResolveAsync(reference);

        // The arithmetic the cache exists for: a sync making dozens of calls asks the vault once.
        Assert.Equal(1, _plugin.GetCalls);
    }

    [Fact]
    public async Task DoesNotConfuseTwoFieldsOfTheSameSecret()
    {
        var created = await CreateAsync();

        Assert.Equal("svc",
            await _svc.ResolveAsync(SecretReference.Create(created.Id, "db-prod", "username")));

        // A cache key built by concatenating the parts would collide here for some inputs; keying on
        // the reference's own canonical form cannot.
        Assert.Equal("p4ss",
            await _svc.ResolveAsync(SecretReference.Create(created.Id, "db-prod", "password")));
    }

    [Fact]
    public async Task RotatingTheApiKeyEvictsWhatThatKeyFetched()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);

        var input = Input();
        input.Id = created.Id;
        await _svc.UpdateConnectionAsync(input, "a-new-api-key");

        await _svc.ResolveAsync(reference);

        // Two calls, not one: the cached value was fetched with a credential that no longer exists.
        Assert.Equal(2, _plugin.GetCalls);
        Assert.Equal("a-new-api-key", _plugin.LastCredentials!.ApiKey);
    }

    [Fact]
    public async Task ResavingAConnectionUnchangedKeepsTheCacheWarm()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);

        var input = Input();
        input.Id = created.Id;

        // Same values, retyped. Comparing the incoming values before normalisation used to read an
        // unset machine ID as a change on every save, throwing away a warm cache — and with it the
        // whole point of caching — for a form nobody had edited.
        await _svc.UpdateConnectionAsync(input, null);

        await _svc.ResolveAsync(reference);

        Assert.Equal(1, _plugin.GetCalls);
    }

    [Fact]
    public async Task ChangingTheMachineIdEvicts()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);

        var input = Input();
        input.Id = created.Id;
        input.MachineId = "machine-42";
        await _svc.UpdateConnectionAsync(input, null);

        // The machine ID is half of what the vault authenticates; a value fetched without it was
        // fetched as a different caller.
        await _svc.ResolveAsync(reference);

        Assert.Equal(2, _plugin.GetCalls);
    }

    [Fact]
    public async Task ChangingTheAppIdEvicts()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);

        var input = Input();
        input.Id = created.Id;
        input.AppId = "netrisk-prod";
        await _svc.UpdateConnectionAsync(input, null);

        // The app id is part of who the vault thinks is asking, so a value fetched under a different
        // one was fetched under different policy.
        await _svc.ResolveAsync(reference);

        Assert.Equal(2, _plugin.GetCalls);
    }

    [Fact]
    public async Task ChangingTheTlsSettingEvicts()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);

        var input = Input();
        input.Id = created.Id;
        input.IgnoreSslErrors = true;
        await _svc.UpdateConnectionAsync(input, null);

        await _svc.ResolveAsync(reference);

        Assert.Equal(2, _plugin.GetCalls);
    }

    [Fact]
    public async Task ChangingTheBaseUrlEvictsToo()
    {
        var created = await CreateAsync();
        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);

        var input = Input();
        input.Id = created.Id;
        input.BaseUrl = "https://other-vault.example.com";
        await _svc.UpdateConnectionAsync(input, null);

        await _svc.ResolveAsync(reference);

        Assert.Equal(2, _plugin.GetCalls);
    }

    [Fact]
    public async Task HonoursAVaultThatAsksForAShorterCacheLife()
    {
        var created = await CreateAsync();
        _plugin.MaxCacheAge = TimeSpan.FromMilliseconds(40);

        var reference = SecretReference.Create(created.Id, "tm-key");

        await _svc.ResolveAsync(reference);
        await Task.Delay(120);
        await _svc.ResolveAsync(reference);

        // The vault wins when it is stricter than the connection's 15 minutes: it knows something
        // about that particular secret that the installation does not.
        Assert.Equal(2, _plugin.GetCalls);
    }

    [Fact]
    public async Task ADisabledConnectionResolvesNothingAndSaysSo()
    {
        var created = await CreateAsync();

        var input = Input();
        input.Id = created.Id;
        input.Enabled = false;
        await _svc.UpdateConnectionAsync(input, null);

        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _svc.ResolveAsync(SecretReference.Create(created.Id, "tm-key")));

        Assert.Contains("disabled", ex.Message);
        Assert.Contains("Prod vault", ex.Message);
    }

    [Fact]
    public async Task AReferenceToAConnectionThatIsGoneFailsWithTheIdNamed()
    {
        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _svc.ResolveAsync(SecretReference.Create(404, "tm-key")));

        Assert.Contains("404", ex.Message);
        Assert.Contains("no longer exists", ex.Message);
    }

    [Fact]
    public async Task AVaultRefusalBecomesAResolutionFailureAndNotAnEmptyCredential()
    {
        var created = await CreateAsync();
        _plugin.FailWith = "this key may not read db-prod";

        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _svc.ResolveAsync(SecretReference.Create(created.Id, "db-prod", "password")));

        Assert.Contains("may not read", ex.Message);
        Assert.Contains("db-prod / password", ex.Message);
    }

    [Fact]
    public async Task AnEmptyValueFromTheVaultIsRefusedRatherThanUsed()
    {
        var created = await CreateAsync();
        _plugin.With("blank", "");

        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _svc.ResolveAsync(SecretReference.Create(created.Id, "blank")));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task AFailedResolutionIsNotCached()
    {
        var created = await CreateAsync();
        _plugin.FailWith = "temporarily unavailable";

        await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _svc.ResolveAsync(SecretReference.Create(created.Id, "tm-key")));

        _plugin.FailWith = null;

        Assert.Equal("vision-one-key",
            await _svc.ResolveAsync(SecretReference.Create(created.Id, "tm-key")));
    }

    // --- describe, usage and deletion -------------------------------------------------------------

    [Fact]
    public async Task DescribesAStoredReferenceForDisplay()
    {
        var created = await CreateAsync();

        var view = await _svc.DescribeAsync(
            SecretReference.Create(created.Id, "db-prod", "password").ToString());

        Assert.True(view.IsVaultReference);
        Assert.True(view.Resolvable);
        Assert.Equal("Prod vault", view.ConnectionName);
        Assert.Equal("db-prod", view.SecretId);
        Assert.Equal("password", view.Field);
        Assert.Contains("Prod vault", view.DisplayName);

        // Describing a field must not talk to the vault: a form with six credential fields would
        // otherwise make six network round trips before it renders.
        Assert.Equal(0, _plugin.GetCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-literal-api-key")]
    public async Task DescribesALiteralAsNotAReference(string? value)
    {
        var view = await _svc.DescribeAsync(value);

        Assert.False(view.IsVaultReference);
        Assert.False(view.Resolvable);
    }

    [Fact]
    public async Task DescribesAReferenceToAMissingConnectionAsUnresolvable()
    {
        var view = await _svc.DescribeAsync(SecretReference.Create(99, "x").ToString());

        Assert.True(view.IsVaultReference);
        Assert.False(view.Resolvable);
        Assert.Contains("missing", view.DisplayName);
    }

    [Fact]
    public async Task CountsTheFieldsThatResolveThroughAConnection()
    {
        var created = await CreateAsync();

        var reference = SecretReference.Create(created.Id, "tm-key").ToString();

        Seed(ctx =>
        {
            ctx.TrendMicroConnections.Add(new TrendMicroConnection
            {
                Id = 1, Name = "tm", Region = "us", BaseUrl = "https://x", CreatedAt = DateTime.UtcNow,
                EncryptedApiKey = reference
            });

            ctx.IssueTrackerConnections.Add(new IssueTrackerConnection
            {
                Id = 1, Name = "jira", Provider = IssueTrackerProviderKind.Jira,
                BaseUrl = "https://j", ProjectKey = "SEC", CreatedAt = DateTime.UtcNow,
                EncryptedToken = reference
            });

            ctx.NotificationChannels.Add(new NotificationChannel
            {
                Id = 1, Name = "slack", Kind = NotificationChannelKind.Slack,
                CreatedAt = DateTime.UtcNow,
                ConfigurationJson = $$"""{"WebhookUrl":"{{reference}}"}"""
            });
        });

        Assert.Equal(3, await _svc.CountReferencesAsync(created.Id));
    }

    [Fact]
    public async Task RefusesToDeleteAConnectionThatFieldsStillResolveThrough()
    {
        var created = await CreateAsync();

        Seed(ctx => ctx.TrendMicroConnections.Add(new TrendMicroConnection
        {
            Id = 1, Name = "tm", Region = "us", BaseUrl = "https://x", CreatedAt = DateTime.UtcNow,
            EncryptedApiKey = SecretReference.Create(created.Id, "tm-key").ToString()
        }));

        // There is no foreign key that could stop this — a reference lives inside a credential column —
        // so this check is the only thing between a delete and a Vision One sync that fails at 3am.
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.DeleteConnectionAsync(created.Id));

        Assert.Contains("1 credential field", ex.Message);
        Assert.Contains("Prod vault", ex.Message);
    }

    [Fact]
    public async Task DeletesAConnectionNothingPointsAt()
    {
        var created = await CreateAsync();

        await _svc.DeleteConnectionAsync(created.Id);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetConnectionAsync(created.Id));
    }

    [Fact]
    public async Task DeletingAConnectionEvictsItsCachedSecrets()
    {
        var created = await CreateAsync();
        await _svc.ResolveAsync(SecretReference.Create(created.Id, "tm-key"));

        Assert.Equal(1, _cache.Count);

        await _svc.DeleteConnectionAsync(created.Id);

        Assert.Equal(0, _cache.Count);
    }

    // --- availability ---------------------------------------------------------------------------

    [Fact]
    public async Task IsNotAvailableWithNoConnections()
    {
        Assert.False(await _svc.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailableOnceAnEnabledConnectionExistsForAnEnabledPlugin()
    {
        await CreateAsync();

        Assert.True(await _svc.IsAvailableAsync());
    }

    [Fact]
    public async Task IsNotAvailableWhenThePluginIsDisabled()
    {
        await CreateAsync();

        ArrangePluginInstalled(enabled: false);

        // The desktop client hides every picker button on this answer, which is right: a bound field
        // would fail to resolve and a new binding could not be tested.
        Assert.False(await _svc.IsAvailableAsync());
    }

    [Fact]
    public async Task IsNotAvailableWhenEveryConnectionIsDisabled()
    {
        var created = await CreateAsync();

        var input = Input();
        input.Id = created.Id;
        input.Enabled = false;
        await _svc.UpdateConnectionAsync(input, null);

        Assert.False(await _svc.IsAvailableAsync());
    }

    [Fact]
    public async Task CancellationPropagatesRatherThanBecomingAResolutionFailure()
    {
        var created = await CreateAsync();
        _plugin.ThrowUnexpected = new OperationCanceledException();

        // A cancelled job is not a broken vault, and reporting it as one would fill the log with
        // resolution failures every time a sync was stopped.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _svc.ResolveAsync(SecretReference.Create(created.Id, "tm-key"), CancellationToken.None));
    }

    private DAL.Entities.SecretVaultConnection Read(int id)
    {
        using var context = GetService<IDalService>().GetContext();
        return context.SecretVaultConnections.Single(c => c.Id == id);
    }
}
