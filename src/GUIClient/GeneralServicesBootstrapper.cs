using System.Reflection;
using ClientServices.Interfaces;
using ClientServices.Services;
using GUIClient.Tools;
using GUIClient.Tools.Camera;
using GUIClient.ViewModels.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Model.Configuration;

namespace GUIClient;

public class GeneralServicesBootstrapper
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ILocalizationService>(sp => new LocalizationService(
            sp.GetRequiredService<ILoggerFactory>(),
            Assembly.GetAssembly(typeof(GeneralServicesBootstrapper))!));

        services.AddSingleton<IRegistrationService>(sp => new RegistrationService(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IMutableConfigurationService>(),
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IAuthenticationService>(sp => new AuthenticationRestService(
            sp.GetRequiredService<IRegistrationService>(),
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IMutableConfigurationService>(),
            sp.GetRequiredService<IEnvironmentService>()));

        services.AddSingleton<IClientService>(sp => new ClientService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<CameraManager>(sp => new CameraManager(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IFaceIDService>(),
            sp.GetRequiredService<ILocalizationService>().GetLocalizer(typeof(CameraManager).Assembly)));

        services.AddSingleton<PluginManager>(sp => new PluginManager(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IPluginsService>(),
            sp.GetRequiredService<IAuthenticationService>(),
            sp.GetRequiredService<IFaceIDService>(),
            sp.GetRequiredService<IMemoryCacheService>()));

        services.AddSingleton<IMemoryCacheService, MemoryCacheService>();
        services.AddSingleton<ConstantManager>();
        services.AddSingleton<IMainWindowProvider, MainWindowProvider>();
        services.AddSingleton<IDialogService>(sp => new DialogService(
            sp.GetRequiredService<IMainWindowProvider>()));

        services.AddSingleton<IStatisticsService>(sp => new StatisticsRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<IAssessmentsService>(sp => new AssessmentsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IRestService>(sp => new RestService(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<ServerConfiguration>(),
            sp.GetRequiredService<IEnvironmentService>(),
            sp.GetRequiredService<IMutableConfigurationService>()));

        services.AddSingleton<IRisksService>(sp => new RisksRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<ITeamsService>(sp => new TeamsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IRolesService>(sp => new RolesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IMitigationService>(sp => new MitigationRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<IUsersService>(sp => new UsersRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IFilesService>(sp => new FilesRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<IPluginsService>(sp => new PluginsRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<IEntitiesService>(sp => new EntitiesRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>(),
            sp.GetRequiredService<IMemoryCacheService>()));

        services.AddSingleton<IMgmtReviewsService>(sp => new MgmtReviewsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<ISystemService>(sp => new SystemRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IVulnerabilitiesService>(sp => new VulnerabilitiesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IIncidentsService>(sp => new IncidentsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IFaceIDService>(sp => new FaceIDRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<ICommentsService>(sp => new CommentsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IConfigurationsService>(sp => new ConfigurationsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IHostsService>(sp => new HostsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<ITechnologiesService>(sp => new TechnologiesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IReportsService>(sp => new ReportsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IListLocalizationService>(sp => new ListLocalizationService(
            typeof(GeneralServicesBootstrapper).Assembly));

        services.AddSingleton<IImpactsService>(sp => new ImpactsRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IListLocalizationService>()));

        services.AddTransient<IVulnerabilityImporterService, VulnerabilityImporterService>();

        services.AddTransient<IMessagesService>(sp => new MessagesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IFixRequestsService>(sp => new FixRequestsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IEmailsService>(sp => new EmailsRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IIncidentResponsePlansService>(sp => new IncidentResponsePlansRestService(
            sp.GetRequiredService<IRestService>()));
    }
}
