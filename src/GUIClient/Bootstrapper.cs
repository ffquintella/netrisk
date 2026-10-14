using Microsoft.Extensions.DependencyInjection;

namespace GUIClient;

public class Bootstrapper
{
    public static void Register(IServiceCollection services, string environment)
    {
        EnvironmentServicesBootstrapper.RegisterEnvironmentServices(services, environment);
        ConfigurationBootstrapper.RegisterConfiguration(services, environment);
        LoggingBootstrapper.RegisterLogging(services);
        MapperBootstrapper.RegisterServices(services);
        GeneralServicesBootstrapper.RegisterServices(services);
    }
}
