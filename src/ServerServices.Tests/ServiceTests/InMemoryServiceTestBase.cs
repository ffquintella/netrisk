using System;
using API;
using DAL.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ServerServices.Interfaces;
using ServerServices.Findings;
using ServerServices.Importers;
using ServerServices.Importers.Dedup;
using ServerServices.Governance;
using ServerServices.Integrations;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Sieve.Models;
using Sieve.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Base class for service tests that run the real domain services against an EF Core
/// in-memory database. Each test instance gets an isolated database; seed data with
/// <see cref="Seed"/> and resolve the service-under-test from <see cref="ServiceProvider"/>.
/// </summary>
public abstract class InMemoryServiceTestBase
{
    protected readonly IServiceProvider ServiceProvider;
    private readonly InMemoryDalService _dalService;

    private static readonly object MapsterLock = new();
    private static bool _mapsterReady;

    private static void EnsureMapsterConfigured()
    {
        lock (MapsterLock)
        {
            if (_mapsterReady) return;
            // Mirror the API startup so same-type .Adapt() calls in the services use the
            // configured maps. Tests pass detached entities (no circular navigations) to the
            // services' Update methods to avoid Mapster recursing through EF fixup cycles.
            ServerServices.MapsterConfiguration.RegisterMappings();
            _mapsterReady = true;
        }
    }

    protected InMemoryServiceTestBase()
    {
        EnsureMapsterConfigured();

        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        var factory = new SerilogLoggerFactory(logger);
        Log.Logger = logger;

        _dalService = new InMemoryDalService(Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(logger);
        services.AddSingleton<IDalService>(_dalService);
        services.AddSingleton(MockConfiguration.Create());
        services.AddScoped<ISieveProcessor, ApplicationSieveProcessor>();
        services.AddSingleton<ILocalizationService>(
            new LocalizationService(factory, typeof(ApplicationSieveProcessor).Assembly));

        // Mocks for I/O-bound collaborators.
        services.AddTransient<IEmailService, EmailMock>();
        services.AddTransient<IFilesService, FilesServiceMock>();

        // Real domain services under test.
        services.AddTransient<IRolesService, RolesService>();
        services.AddTransient<IPermissionsService, PermissionsService>();
        services.AddTransient<IUsersService, UsersService>();
        services.AddTransient<IRisksService, RisksService>();
        services.AddTransient<IVulnerabilitiesService, VulnerabilitiesService>();
        services.AddTransient<IEntitiesService, EntitiesService>();
        services.AddTransient<IHostsService, HostsService>();
        services.AddTransient<IClientRegistrationService, ClientRegistrationService>();
        services.AddTransient<ICommentsService, CommentsService>();
        services.AddTransient<IMessagesService, MessagesService>();
        services.AddTransient<IIncidentResponsePlansService, IncidentResponsePlansService>();
        services.AddTransient<IIncidentsService, IncidentsService>();
        services.AddTransient<IIrpAutomationService, IrpAutomationService>();
        services.AddTransient<IAssessmentsService, AssessmentsService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
        services.AddSingleton<IMasterDashboardService, MasterDashboardService>();
        services.AddTransient<IIrpScheduleService, IrpScheduleService>();
        services.AddTransient<IMitigationsService, MitigationsService>();
        services.AddTransient<IMgmtReviewsService, MgmtReviewsService>();
        services.AddTransient<IRiskCalculationService, RiskCalculationService>();
        services.AddTransient<ITeamsService, TeamsService>();
        services.AddTransient<IJobsService, JobsService>();
        services.AddTransient<ISettingsService, SettingsService>();
        services.AddTransient<IFixRequestsService, FixRequestsService>();
        services.AddTransient<ILinksService, LinksService>();
        services.AddTransient<ITechnologiesService, TechnologiesService>();
        services.AddTransient<IConfigurationsService, ConfigurationsService>();
        services.AddTransient<IImpactsService, ImpactsService>();
        services.AddTransient<IBiometricTransactionsService, BiometricTransactionsService>();
        services.AddTransient<IReportsService, ReportsService>();
        services.AddTransient<IExportService, ExportService>();
        services.AddTransient<IQuestPdfRenderingService, QuestPdfRenderingService>();
        services.AddTransient<IImportsService, ImportsService>();

        // Track 3 (ASPM). PluginsService is real here but harmless: it walks a plugins directory
        // that does not exist in a test run and contributes no strategies or importers, which is
        // exactly the built-ins-only configuration these tests want.
        services.AddTransient<IPluginsService, PluginsService>();
        services.AddTransient<IDeduplicationService, DeduplicationService>();
        services.AddTransient<IImporterRegistry, ImporterRegistry>();
        services.AddTransient<ISlaService, SlaService>();
        services.AddTransient<IFindingLifecycleService, FindingLifecycleService>();
        services.AddTransient<IFindingIngestionService, FindingIngestionService>();
        services.AddTransient<IApiTokensService, ApiTokensService>();

        // Track 4 (Integrations). The full graph is registered so the services under test resolve, with
        // one deliberate substitution: the outbound HTTP client is a fake, so no test can reach a real
        // host even if a provider is invoked by accident.
        services.AddSingleton<IOutboundHttpClient>(FakeOutboundHttpClient);
        services.AddTrack4Integrations(includeOutboundHttp: false);

        // Track 8 (Risk governance). Registered as the hosts do, so a test exercises the same
        // enforcement graph the API and the job host resolve.
        services.AddTrack8Governance();

        // A protector over a fixed root secret rather than the install's key file: a test must not
        // create files under the user's application-data folder, and a deterministic key means an
        // encrypted fixture round-trips.
        services.AddSingleton<ISecretProtector>(
            new ServerServices.Security.SecretProtector(logger, "netrisk-test-root-secret"));

        services.Configure<SieveOptions>(sieveOptions =>
        {
            sieveOptions.DefaultPageSize = 100;
            sieveOptions.MaxPageSize = 1000;
            sieveOptions.ThrowExceptions = true;
            sieveOptions.CaseSensitive = false;
            sieveOptions.IgnoreNullsOnNotEqual = true;
        });

        ServiceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// The fake outbound HTTP client every integration in these tests talks to. Exposed so a test can
    /// script a response; by default it answers 200 with an empty JSON object, which is enough for the
    /// paths that only care that a call succeeded.
    /// </summary>
    protected readonly FakeOutboundHttpClient FakeOutboundHttpClient = new();

    protected T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();

    /// <summary>Seeds entities into the shared in-memory database.</summary>
    protected void Seed(Action<AuditableContext> seed)
    {
        using var context = _dalService.GetContext();
        seed(context);
        context.SaveChanges();
    }

    /// <summary>Opens a fresh context for assertions against the database state.</summary>
    protected AuditableContext OpenContext() => _dalService.GetContext();

    /// <summary>
    /// Narrows every context the services open to <paramref name="entityIds"/>, standing in for a
    /// user whose claims carry those entity assignments (Track 2 milestone 2.3.2).
    /// </summary>
    protected void ScopeTo(params int[] entityIds) => _dalService.Scope = EntityScope.ForEntities(entityIds);

    /// <summary>Stands in for an authenticated user with no entity assignment at all.</summary>
    protected void ScopeToNothing() => _dalService.Scope = EntityScope.DenyAll;

    /// <summary>Restores the unrestricted scope a global administrator or a background job gets.</summary>
    protected void ScopeToEverything() => _dalService.Scope = EntityScope.Unrestricted;

    /// <summary>Seeds bypassing the scope filter, so a test can plant another entity's data.</summary>
    protected void SeedUnscoped(Action<AuditableContext> seed)
    {
        using var context = _dalService.GetContext(bypassEntityScope: true);
        seed(context);
        context.SaveChanges();
    }
}
