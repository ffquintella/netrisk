using GUIClient.Navigation;
using GUIClient.Notifications;
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

        // GitHub #80 (S44) — the comment and evidence on each answer of an assessment run.
        services.AddSingleton<IAssessmentEvidenceService>(sp => new AssessmentEvidenceRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IFilesService>()));

        services.AddSingleton<IRestService>(sp => new RestService(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<ServerConfiguration>(),
            sp.GetRequiredService<IEnvironmentService>(),
            sp.GetRequiredService<IMutableConfigurationService>()));

        services.AddSingleton<IRisksService>(sp => new RisksRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<ITeamsService>(sp => new TeamsRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IMemoryCacheService>()));

        services.AddSingleton<IRolesService>(sp => new RolesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddSingleton<IMitigationService>(sp => new MitigationRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddSingleton<IUsersService>(sp => new UsersRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IMemoryCacheService>()));

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
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IMemoryCacheService>()));

        // Track 3 (ASPM) administration: dedup heuristics, SLA policy, risk acceptances, CI tokens.
        services.AddTransient<IFindingsAdminService>(sp => new FindingsAdminRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 8 (Risk governance): acceptance, appetite, the audit trail, treatment tasks,
        // pending-risk triage and quantitative scoring.
        services.AddTransient<IRiskGovernanceService>(sp => new RiskGovernanceRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 9 Stage 9.1 (S41): the risk linkage chain and the critical-process coverage metric.
        services.AddTransient<IRiskChainService>(sp => new RiskChainRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 9 Stage 9.3 (S43): business impact analysis, cascading dependencies, restoration tests.
        services.AddTransient<IContinuityService>(sp => new ContinuityRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 9 Stage 9.4 (S45): exploitation signals — KEV, EPSS, ATT&CK and the prioritization. The
        // desktop surface that uses it is T302.
        services.AddTransient<IExploitationSignalsService>(sp => new ExploitationSignalsRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 9 Stage 9.5 (S46): the eleven flags, Gate A, the Phase 4 decision and Top Risks. The desktop
        // surface that uses it is T303.
        services.AddTransient<IRiskFlagsService>(sp => new RiskFlagsRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 9 Stage 9.6 (S47): treatment economics — the option and monetary cost, Gate C, the target level and
        // Gate D. The desktop surface that uses it is T304.
        services.AddTransient<ITreatmentEconomicsService>(sp => new TreatmentEconomicsRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 9 Stage 9.7 (S48): tail statistics and portfolio — the tail of a risk, its loss components, the
        // declared correlations, the portfolio aggregation and the appetite's monetary tolerances.
        services.AddTransient<ITailRiskService>(sp => new TailRiskRestService(
            sp.GetRequiredService<IRestService>()));

        // Track 4 (Integrations) administration: notification channels and subscriptions, issue
        // trackers, identity providers, SCIM tokens, and the two posture providers.
        services.AddTransient<IIntegrationsService>(sp => new IntegrationsRestService(
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

        services.AddTransient<IReportTemplatesService>(sp => new ReportTemplatesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IReportSchedulesService>(sp => new ReportSchedulesRestService(
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
            
        services.AddTransient<IUserAccessService>(sp => new UserAccessRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IIrpTemplatesService>(sp => new IrpTemplatesRestService(
            sp.GetRequiredService<IRestService>()));

        services.AddTransient<IDashboardService>(sp => new DashboardRestService(
            sp.GetRequiredService<IRestService>(),
            sp.GetRequiredService<IAuthenticationService>()));

        services.AddTransient<IExportClientService>(sp => new ExportClientService(sp.GetRequiredService<IRestService>()));

        // Single route into the shell's view stack and its auxiliary windows (IX-7).
        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(sp.GetRequiredService<IMainWindowProvider>()));

        // Transient-feedback channel for IX-4. Singleton: the shell binds one toast host to it.
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());

        // Dialog view-models are resolved by name from the DI container by DialogService
        // (Program.ServiceProvider.GetRequiredService). Register every concrete dialog
        // view-model so opening any dialog doesn't throw "No service for type ...".
        RegisterDialogViewModels(services);
    }

    /// <summary>
    /// Registers all concrete view-models deriving from <see cref="DialogViewModelBase{TResult}"/>
    /// as transient services. <see cref="ViewModels.Dialogs.DialogService"/> resolves dialog
    /// view-models from the container by type, so each must be registered.
    /// </summary>
    private static void RegisterDialogViewModels(IServiceCollection services)
    {
        var assembly = Assembly.GetAssembly(typeof(GeneralServicesBootstrapper))!;

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition || !IsDialogViewModel(type))
                continue;

            services.AddTransient(type);
        }
    }

    private static bool IsDialogViewModel(System.Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == typeof(DialogViewModelBase<>))
            {
                return true;
            }
        }

        return false;
    }
}
