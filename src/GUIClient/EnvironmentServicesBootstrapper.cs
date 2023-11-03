using ClientServices.Services;
using ClientServices.Interfaces;
using Splat;

namespace GUIClient;

public class EnvironmentServicesBootstrapper
{
    public static void RegisterEnvironmentServices(IMutableDependencyResolver services, 
        IReadonlyDependencyResolver resolver, string environment)
    {
        RegisterCommonServices(services, environment);
        RegisterPlatformSpecificServices(services, resolver);
    }

    private static void RegisterCommonServices(IMutableDependencyResolver services, string environment)
    {
        services.RegisterLazySingleton<IEnvironmentService>(() => new EnvironmentService(environment));
        services.Register<IPlatformService>(() => new PlatformService());
    }

    private static void RegisterPlatformSpecificServices(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {

    }

    private static void RegisterWindowsServices(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {

    }
}