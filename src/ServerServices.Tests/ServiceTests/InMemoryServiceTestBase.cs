using System;
using API;
using DAL.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ServerServices.Interfaces;
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
        services.AddTransient<IAssessmentsService, AssessmentsService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
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
}
