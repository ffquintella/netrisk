using ClientServices.Services;
using ClientServices.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient;

public class EnvironmentServicesBootstrapper
{
    public static void RegisterEnvironmentServices(IServiceCollection services, string environment)
    {
        RegisterCommonServices(services, environment);
        RegisterPlatformSpecificServices(services);
    }

    private static void RegisterCommonServices(IServiceCollection services, string environment)
    {
        services.AddSingleton<IEnvironmentService>(_ => new EnvironmentService(environment));
        services.AddSingleton<IPlatformService, PlatformService>();
    }

    private static void RegisterPlatformSpecificServices(IServiceCollection services)
    {
    }

    private static void RegisterWindowsServices(IServiceCollection services)
    {
    }
}
