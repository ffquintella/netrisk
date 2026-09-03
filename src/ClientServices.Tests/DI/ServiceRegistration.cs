using System;
using System.Collections.Generic;
using System.Linq;
using ClientServices;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RestSharp;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace ClientServices.Tests.DI;

public class ServiceRegistration
{
    /// <param name="configure">
    /// Applied after everything else, so anything it registers wins. A test class supplies its own
    /// <see cref="StubRestBackend"/> here rather than adding routes to a mock every other test also
    /// sees.
    /// </param>
    public static IServiceProvider GetServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;

        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());

        RegisterClientServices(services);

        // Collaborators whose real implementations need something the container cannot supply (a
        // server URL, an Assembly, a config file). Registered after the convention scan so they win.
        var mockClient = MockSetup.GetRestClient();
        services.AddSingleton<IRestClient>(mockClient);
        services.AddSingleton<IRestService>(MockSetup.GetRestService());
        services.AddSingleton<IEnvironmentService>(new EnvironmentService("production"));
        services.AddSingleton(Substitute.For<IListLocalizationService>());
        services.AddSingleton(Substitute.For<IMemoryCacheService>());

        // SystemRestService pulls IConfiguration out of the static accessor in its constructor, and
        // LocalizationService takes the assembly it should read resources from.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            // string?, because that is what AddInMemoryCollection takes: a configuration value is
            // legitimately absent.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Server"] = "https://localhost:5443",
                ["Client:Version"] = "2.15.0"
            })
            .Build());
        services.AddSingleton(typeof(RestServiceBase).Assembly);

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        ServiceProviderAccessor.Provider = provider;
        return provider;
    }

    /// <summary>
    /// Registers every concrete service in <c>ClientServices.Services</c> against the
    /// <c>ClientServices.Interfaces</c> interfaces it implements, so covering a new REST service
    /// needs no edit to this file.
    /// </summary>
    private static void RegisterClientServices(IServiceCollection services)
    {
        var implementations = typeof(RestServiceBase).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && t.Namespace == "ClientServices.Services");

        foreach (var implementation in implementations)
        {
            foreach (var contract in implementation.GetInterfaces()
                         .Where(i => i.Namespace == "ClientServices.Interfaces"))
            {
                services.AddTransient(contract, implementation);
            }
        }
    }

    /// <summary>The service interfaces the convention scan found an implementation for.</summary>
    public static IEnumerable<Type> DiscoverableContracts()
    {
        return typeof(RestServiceBase).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && t.Namespace == "ClientServices.Services")
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.Namespace == "ClientServices.Interfaces")
            .Distinct();
    }
}
