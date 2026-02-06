using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ClientServices.Services;
using ClientServices.Interfaces;
using Model.Configuration;

namespace GUIClient;

public class ConfigurationBootstrapper
{
    public static void RegisterConfiguration(IServiceCollection services)
    {
        var configuration = BuildConfiguration();

        RegisterConfiguration(services, configuration);
        RegisterLoggingConfiguration(services, configuration);
        RegisterLanguagesConfiguration(services, configuration);
        RegisterServerConfiguration(services, configuration);
        RegisterMutableConfiguration(services);
    }

#if DEBUG
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.development.json")
            .AddUserSecrets<Program>()
            .Build();
#else
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();
#endif

    private static void RegisterMutableConfiguration(IServiceCollection services)
    {
        services.AddSingleton<IMutableConfigurationService>(sp =>
            new MutableConfigurationService(sp.GetRequiredService<IEnvironmentService>()));
    }

    private static void RegisterConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);
    }

    private static void RegisterServerConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        var config = new ServerConfiguration();
        configuration.GetSection("Server").Bind(config);
        services.AddSingleton(config);
    }

    private static void RegisterLoggingConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        var config = new LoggingConfiguration();
        configuration.GetSection("Logging").Bind(config);
        services.AddSingleton(config);
    }

    private static void RegisterLanguagesConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        var config = new LanguagesConfiguration();
        configuration.GetSection("Languages").Bind(config);
        services.AddSingleton(config);
    }
}
