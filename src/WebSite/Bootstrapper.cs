namespace WebSite;

public static class Bootstrapper
{
    public static void Register(IServiceCollection services, IConfiguration config)
    {
        LoggingBootstrapper.RegisterLogging(services, config);
        ServicesBootstrapper.RegisterServices(services, config);
        //AuthenticationBootstrapper.RegisterAuthentication(services, config);
    }
}