using System.Collections.Generic;
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
        AddSources(new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()),
                ClientConfigurationSources.Development)
            .AddUserSecrets<Program>()
            .Build();
#else
    private static IConfiguration BuildConfiguration() =>
        AddSources(new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()),
                ClientConfigurationSources.Release)
            .Build();
#endif

    /// <summary>
    /// Layers the declared configuration files in order. The last one is the optional
    /// administrator overlay (netrisk.ini) that installers write, so a deployed setting wins
    /// over the shipped default.
    /// </summary>
    private static IConfigurationBuilder AddSources(
        IConfigurationBuilder builder,
        IReadOnlyList<ClientConfigurationSource> sources)
    {
        foreach (var source in sources)
        {
            builder = source.Format switch
            {
                ClientConfigurationFormat.Ini => builder.AddIniFile(source.FileName, source.Optional),
                _ => builder.AddJsonFile(source.FileName, source.Optional)
            };
        }

        return builder;
    }

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
