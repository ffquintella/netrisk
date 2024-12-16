using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API;

public static class Bootstrapper
{
    public static void Register(IServiceCollection services, 
        IConfiguration config)
    {
        var factory = LoggingBootstrapper.RegisterLogging(services, config);
        ConfigurationBootstrapper.RegisterConfiguration(services, config);
        LocalizationBootstrapper.RegisterLocalization(services, config, factory);
        ServicesBootstrapper.RegisterServices(services, config);
        AuthenticationBootstrapper.RegisterAuthentication(services, config);
        
    }
}