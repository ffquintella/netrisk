using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog;
using ServerServices.Integrations;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// That the shared Track 4 registration actually composes the vault graph.
///
/// This is here because of the way the gap first showed up. <c>ISecretResolver</c> sits on the
/// credential read path of every integration, and it reaches <c>ISecretVaultService</c>, which reaches
/// <c>IPluginsService</c>, which reaches <c>ISettingsService</c>. A host that registered Track 4
/// without the last two started fine and passed every test that did not touch a credential — and would
/// have failed on the first notification send in production. Resolving the graph in a test is what
/// turns that into a build failure.
/// </summary>
[TestSubject(typeof(IntegrationServiceRegistration))]
public class SecretVaultRegistrationTest
{
    /// <summary>
    /// The minimum a host supplies before calling <c>AddTrack4Integrations</c>: a logger, a database
    /// and configuration. If the vault graph ever needs more than that, this test says so.
    /// </summary>
    private static ServiceProvider Compose()
    {
        var logger = new LoggerConfiguration().CreateLogger();

        var services = new ServiceCollection();

        services.AddSingleton<ILogger>(logger);
        services.AddSingleton(MockDalService.Create());
        services.AddSingleton<IConfiguration>(MockConfiguration.Create());
        services.AddSingleton<ISecretProtector>(new SecretProtector(logger, "netrisk-test-root-secret"));
        services.AddSingleton<IOutboundHttpClient>(new FakeOutboundHttpClient());

        services.AddTrack4Integrations(includeOutboundHttp: false);

        // ValidateOnBuild would be stricter still, but the Track 4 graph legitimately contains services
        // whose own dependencies a host supplies; resolving the vault chain explicitly is the part that
        // matters here.
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheResolverAndItsWholeChainResolve()
    {
        using var provider = Compose();

        Assert.NotNull(provider.GetRequiredService<ISecretResolver>());
        Assert.NotNull(provider.GetRequiredService<ISecretVaultService>());
        Assert.NotNull(provider.GetRequiredService<IPluginsService>());
        Assert.NotNull(provider.GetRequiredService<Contracts.Secrets.IPluginHttpClient>());
    }

    [Fact]
    public void TheObfuscatedCacheIsASingleton()
    {
        using var provider = Compose();

        // Not cosmetic. The cache's obfuscation key is per instance, so a transient registration would
        // encrypt every entry under a key discarded before anything could read it — a cache with a
        // 100% miss rate and a vault round trip per request, with nothing failing to say so.
        Assert.Same(provider.GetRequiredService<IObfuscatedSecretCache>(),
            provider.GetRequiredService<IObfuscatedSecretCache>());
    }

    [Fact]
    public void AHostThatSuppliedItsOwnPluginsServiceKeepsIt()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var services = new ServiceCollection();

        var own = Substitute.For<IPluginsService>();

        services.AddSingleton<ILogger>(logger);
        services.AddSingleton(MockDalService.Create());
        services.AddSingleton<IConfiguration>(MockConfiguration.Create());
        services.AddSingleton<ISecretProtector>(new SecretProtector(logger, "netrisk-test-root-secret"));
        services.AddSingleton<IOutboundHttpClient>(new FakeOutboundHttpClient());
        services.AddSingleton(own);

        services.AddTrack4Integrations(includeOutboundHttp: false);

        using var provider = services.BuildServiceProvider();

        // TryAdd, not Add: the API registers a singleton PluginsService of its own, and silently
        // replacing it would give the API two plugin load passes with different state.
        Assert.Same(own, provider.GetRequiredService<IPluginsService>());
    }
}
