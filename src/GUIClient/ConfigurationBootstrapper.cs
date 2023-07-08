using System.IO;
using Microsoft.Extensions.Configuration;
using Splat;
using ClientServices.Services;
using ClientServices.Interfaces;
using Model.Configuration;

namespace GUIClient;

public  class ConfigurationBootstrapper: BaseBootstrapper
{
    
    public static void RegisterConfiguration(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {
        var configuration = BuildConfiguration();

        RegisterConfiguration(services, configuration);
        RegisterLoggingConfiguration(services, configuration);
        RegisterLanguagesConfiguration(services, configuration);
        RegisterServerConfiguration(services, configuration);
        RegisterMutableConfiguration(services, configuration);

    }

    //private static string BaseDirectory { get; set; } = Path.GetDirectoryName( typeof(ConfigurationBootstrapper).Assembly.Location )!;
    
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
    
    private static void RegisterMutableConfiguration(IMutableDependencyResolver services,
        IConfiguration configuration)
    {
        services.RegisterLazySingleton<IMutableConfigurationService>(() => new MutableConfigurationService(GetService<IEnvironmentService>()));
    }
    

    private static void RegisterConfiguration(IMutableDependencyResolver services,
        IConfiguration configuration)
    {
        services.RegisterConstant(configuration);
    }
    
    private static void RegisterServerConfiguration(IMutableDependencyResolver services,
        IConfiguration configuration)
    {
        var config = new ServerConfiguration();
        configuration.GetSection("Server").Bind(config);
        services.RegisterConstant(config);
    }
    
    private static void RegisterLoggingConfiguration(IMutableDependencyResolver services,
        IConfiguration configuration)
    {
        var config = new LoggingConfiguration();
        configuration.GetSection("Logging").Bind(config);
        services.RegisterConstant(config);
    }
    
    private static void RegisterLanguagesConfiguration(IMutableDependencyResolver services,
        IConfiguration configuration)
    {
        var config = new LanguagesConfiguration();
        configuration.GetSection("Languages").Bind(config);
        services.RegisterConstant(config);
    }
    

}