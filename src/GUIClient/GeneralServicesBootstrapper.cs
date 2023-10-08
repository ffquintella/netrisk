using ClientServices.Interfaces;
using ClientServices.Services;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
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
        
        services.RegisterLazySingleton<IAuthenticationService>(() => new AuthenticationRestService(
            GetService<IRegistrationService>(),
            GetService<IRestService>(),
            GetService<IMutableConfigurationService>(),
            GetService<IEnvironmentService>()
            ));

        services.RegisterLazySingleton<IClientService>(() => new ClientService(
            GetService<IRestService>()
        ));
        
        services.RegisterLazySingleton<IMainWindowProvider>(() => new MainWindowProvider(
        ));
        
        services.RegisterLazySingleton<IDialogService>(() => new DialogService(
            GetService<IMainWindowProvider>() 
        ));
        
        services.RegisterLazySingleton<IStatisticsService>(() => new StatisticsRestService(
            GetService<IRestService>(), 
            GetService<IAuthenticationService>()
        ));
        
        services.RegisterLazySingleton<IAssessmentsService>(() => new AssessmentsRestService(GetService<IRestService>()));

        services.RegisterLazySingleton<IRestService>(() => new RestService(
            GetService<ILoggerFactory>(),
            GetService<ServerConfiguration>(),
            GetService<IEnvironmentService>()
        ));
        
        services.RegisterLazySingleton<IRisksService>(() => new RisksRestService(
            GetService<IRestService>(), 
            GetService<IAuthenticationService>()));
        
        services.RegisterLazySingleton<ITeamsService>(() => new TeamsRestService(
            GetService<IRestService>()));
        
        services.RegisterLazySingleton<IRolesService>(() => new RolesRestService(
            GetService<IRestService>()));
        
        services.RegisterLazySingleton<IMitigationService>(() => new MitigationRestService(
            GetService<IRestService>(), 
            GetService<IAuthenticationService>()));
        
        services.RegisterLazySingleton<IUsersService>(() => new UsersRestService(
            GetService<IRestService>()
            ));
        
       
        services.RegisterLazySingleton<IFilesService>(() => new FilesRestService(
            GetService<IRestService>(),
            GetService<IAuthenticationService>()
        ));
        
        services.RegisterLazySingleton<IEntitiesService>(() => new EntitiesRestService(
            GetService<IRestService>(),
            GetService<IAuthenticationService>()
        ));
        
        services.RegisterLazySingleton<IMgmtReviewsService>(() => new MgmtReviewsRestService(
            GetService<IRestService>()
        ));
        
        services.RegisterLazySingleton<ISystemService>(() => new SystemRestService(
            GetService<IRestService>()
        ));
        
        services.Register<IVulnerabilitiesService>(() => new VulnerabilitiesRestService(
            GetService<IRestService>()
        ));
        services.Register<IHostsService>(() => new HostsRestService(
            GetService<IRestService>()
        ));
        services.Register<ITechnologiesService>(() => new TechnologiesRestService(
            GetService<IRestService>()
        ));
        services.Register<IListLocalizationService>(() => new ListLocalizationService(
            typeof(GeneralServicesBootstrapper).Assembly
        ));
        services.Register<IImpactsService>(() => new ImpactsRestService(
            GetService<IRestService>(), GetService<IListLocalizationService>()
        ));
        

    }

    public static void Initialize()
    {
        var mutableConfigurationService = GetService<IMutableConfigurationService>();
        mutableConfigurationService.Initialize();
    }

}