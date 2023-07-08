using ClientServices.Interfaces;
using ClientServices.Services;
using Microsoft.Extensions.Logging;
using Splat;
using Model.Configuration;

namespace GUIClient;

public class GeneralServicesBootstrapper: BaseBootstrapper
{
    
    public static void RegisterServices(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {

        services.RegisterLazySingleton<ILocalizationService>(() => new LocalizationService(GetService<ILoggerFactory>()));
        
        services.RegisterLazySingleton<IRegistrationService>(() => 
            new RegistrationService(GetService<ILoggerFactory>(), 
                GetService<IMutableConfigurationService>(),
                GetService<IRestService>()
                ));
        
        services.RegisterLazySingleton<IAuthenticationService>(() => new AuthenticationService(
            GetService<IRegistrationService>(),
            GetService<IRestService>(),
            GetService<IMutableConfigurationService>(),
            GetService<IEnvironmentService>()
            ));

        services.RegisterLazySingleton<IClientService>(() => new ClientService(
            GetService<IRestService>()
        ));
        
        services.RegisterLazySingleton<IStatisticsService>(() => new StatisticsService(
            GetService<IRestService>(), 
            GetService<IAuthenticationService>()
        ));
        
        services.RegisterLazySingleton<IAssessmentsService>(() => new AssessmentsService(GetService<IRestService>()));

        services.RegisterLazySingleton<IRestService>(() => new RestService(
            GetService<ILoggerFactory>(),
            GetService<ServerConfiguration>(),
            GetService<IEnvironmentService>()
        ));
        
        services.RegisterLazySingleton<IRisksService>(() => new RisksService(
            GetService<IRestService>(), 
            GetService<IAuthenticationService>()));
        
        services.RegisterLazySingleton<ITeamsService>(() => new TeamsService(
            GetService<IRestService>()));
        
        services.RegisterLazySingleton<IMitigationService>(() => new MitigationService(
            GetService<IRestService>(), 
            GetService<IAuthenticationService>()));
        
        services.RegisterLazySingleton<IUsersService>(() => new UsersService(
            GetService<IRestService>()
            ));
    }

}