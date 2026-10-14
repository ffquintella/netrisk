using System;
using System.Reflection;
using System.Security.Claims;
using Mapster;
using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Serilog;
using ServerServices.Security;
using ServerServices.Services;
using ServerServices.Interfaces;
using ServerServices.Findings;
using ServerServices.Importers;
using ServerServices.Importers.Dedup;
using ServerServices.Governance;
using ServerServices.Integrations;
using ILogger = Serilog.ILogger;

namespace BackgroundJobs;

public static class ConfigurationManager
{
    
    public static void ConfigureServices(IServiceCollection services, IConfiguration config, string logDir)
    {
        RegisterServices(services, config);

        ConfigureHangFire(services);
    }

    /// <summary>
    /// The job host's container, without starting Hangfire — <see cref="ConfigureHangFire"/> blocks until
    /// the process quits. Split out so <c>BackgroundJobs.Tests</c> can resolve every recurring job from the
    /// same registrations production uses (T237, S39).
    /// </summary>
    public static void RegisterServices(IServiceCollection services, IConfiguration config)
    {

        services.AddSingleton<IConfiguration>(config);

        // T237 (S39 D-01): the job host's principal reads every entity. Without the scope claim it read
        // DenyAll, and every job saw zero risks, hosts, incidents, findings and campaigns. Granted here
        // and nowhere else — BackgroundScopeContainmentTest pins that.
        services.AddScoped<IHttpContextAccessor>(_ =>
            BackgroundServiceHttpContextAccessor.WithUnrestrictedEntityScope());
        services.AddScoped<IConfigurationsService, ConfigurationsService>();
        services.AddScoped<IFaceIDService, FaceIDService>();
        services.AddScoped<IPluginsService, PluginsService>();
        services.AddScoped<IEnvironmentService, EnvironmentService>();
        services.AddScoped<IUsersService, UsersService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IRolesService, RolesService>();
        services.AddScoped<IPermissionsService, PermissionsService>();
        
        // T292: shared by both DalService registrations below, so an invalidation through either reaches the other.
        services.AddSingleton<ServerServices.Security.IEntityHierarchy, ServerServices.Security.EntityHierarchy>();
        services.AddSingleton<DalService>();
        services.AddSingleton<DatabaseService>();
        
        services.AddSingleton<IDalService, DalService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IFilesService, FilesService>();
        
        services.AddSingleton<IRiskCalculationService, RiskCalculationService>();
        
        ILoggerFactory loggerFactory = new LoggerFactory();

        // Register Localization, QuestPDF, Email, and Report Schedule services
        var localizationService = new LocalizationService(loggerFactory, Assembly.GetExecutingAssembly());
        services.AddSingleton<ILocalizationService>(localizationService);

        services.AddFluentEmail(config["email:from"] ?? "no-reply@netrisk.io")
            .AddRazorRenderer()
            .AddSmtpSender(config["email:smtp:server"] ?? "localhost", int.Parse(config["email:smtp:port"] ?? "25"));
        services.AddTransient<IEmailService, EmailService>();
        services.AddTransient<IQuestPdfRenderingService, QuestPdfRenderingService>();
        // T237 review: the report schedule runs under its creator's scope as it stands at run time, resolved here —
        // never under this host's unrestricted principal (ScheduledReportScopeTest).
        services.AddScoped<IUserEntityScopeResolver, UserEntityScopeResolver>();
        services.AddScoped<ScheduledReportJob>();
        //CLEANUP
        services.AddScoped<AuditCleanup>();
        services.AddScoped<BackupCleanup>();
        services.AddScoped<FileCleanup>();
        services.AddScoped<MessageCleanup>();
        services.AddScoped<TmpCleanup>();
        services.AddScoped<TransactionsCleanup>();
        services.AddScoped<BiometricTransactionCleanup>();
        
        //CALCULATION
        services.AddScoped<ContributingImpactCalculation>();
        services.AddScoped<RiskScoreCalculation>();
        
        //BACKUP
        services.AddScoped<BackupWork>();

        //WEBSITE SYNC
        services.AddScoped<IFixRequestsService, FixRequestsService>();
        services.AddScoped<ICommentsService, CommentsService>();
        services.AddScoped<IMessagesService, MessagesService>();
        services.AddScoped<ITeamsService, TeamsService>();
        services.AddScoped<ILinksService, LinksService>();
        services.AddScoped<IIncidentResponsePlansService, IncidentResponsePlansService>();
        services.AddSingleton<ISyncKeyService, SyncKeyService>();
        services.AddSingleton<ISyncClient, SyncClient>();
        services.AddScoped<ISyncPushBuilder, SyncPushBuilder>();
        services.AddScoped<ISyncIngestService, SyncIngestService>();
        services.AddScoped<BackgroundJobs.Jobs.Sync.SyncBulkJob>();
        services.AddScoped<BackgroundJobs.Jobs.Sync.SyncFastJob>();

        //TRACK 3 (ASPM) — finding lifecycle and SLA automation
        services.AddScoped<IDeduplicationService, DeduplicationService>();
        services.AddScoped<IFindingLifecycleService, FindingLifecycleService>();
        services.AddScoped<ISlaService, SlaService>();
        services.AddScoped<BackgroundJobs.Jobs.Findings.RiskAcceptanceExpiryJob>();
        services.AddScoped<BackgroundJobs.Jobs.Findings.SlaBreachNotificationJob>();

        //TRACK 4 (INTEGRATIONS) — notification dispatch, issue sync, posture providers
        services.AddScoped<IFindingIngestionService, FindingIngestionService>();
        services.AddScoped<IImporterRegistry, ImporterRegistry>();
        services.AddScoped<IPluginsService, PluginsService>();
        services.AddTrack4Integrations();

        // Track 8 (Risk governance). The expiry, cadence and campaign jobs all go through
        // these services, and they have to apply the same rules the API does.
        services.AddTrack8Governance();
        services.AddScoped<BackgroundJobs.Jobs.Integrations.NotificationDispatchJob>();
        services.AddScoped<BackgroundJobs.Jobs.Integrations.IssueSyncPollingJob>();
        services.AddScoped<BackgroundJobs.Jobs.Integrations.TrendMicroSyncJob>();
        services.AddScoped<BackgroundJobs.Jobs.Integrations.SecurityScorecardSyncJob>();
        services.AddScoped<BackgroundJobs.Jobs.Integrations.IntegrationSyncReaperJob>();

        // Track 9 Stage 9.4 (S45) — the CISA KEV and FIRST EPSS syncs. Their service comes from
        // AddTrack4Integrations above, the same registration the API reads through.
        services.AddScoped<BackgroundJobs.Jobs.Integrations.KevCatalogueSyncJob>();
        services.AddScoped<BackgroundJobs.Jobs.Integrations.EpssSyncJob>();

        // Track 9 Stage 9.5 (S46) — the nightly reconciliation of the derived flags. Its service comes from
        // AddTrack8Governance above, the same one the API's Gate A uses.
        services.AddScoped<BackgroundJobs.Jobs.Governance.RiskFlagsDerivationJob>();

        // Track 9 Stage 9.8 (S49) — the daily KRI evaluation. Its service comes from AddTrack8Governance above, the same
        // one the API evaluates a recorded reading with.
        services.AddScoped<BackgroundJobs.Jobs.Governance.KriEvaluationJob>();

        // Track 9 Stage 9.9 (S50) — the quarterly-review notice of the archive. Its service comes from AddTrack8Governance
        // above, the same one the API archives through.
        services.AddScoped<BackgroundJobs.Jobs.Governance.RiskArchiveReviewJob>();

        // T237 (S39 D-01) — the five Track 8 jobs ConfigureGovernanceJobs schedules. They were never
        // registered, and the activator answers an unregistered type with null, so Hangfire failed each run
        // before the job's constructor. RecurringJobsContainerTest resolves every scheduled job from this
        // container, so the next one scheduled and not registered fails a test instead of a night.
        services.AddScoped<BackgroundJobs.Jobs.Governance.ResidualRiskCalculation>();
        services.AddScoped<BackgroundJobs.Jobs.Governance.GovernanceRetentionJob>();
        services.AddScoped<BackgroundJobs.Jobs.Governance.RiskAcceptanceExpiryPass>();
        services.AddScoped<BackgroundJobs.Jobs.Governance.RiskReviewCadenceJob>();
        services.AddScoped<BackgroundJobs.Jobs.Governance.RiskReviewCampaignJob>();

        // What the cadence job reads the register through, and what the website sync (through the response
        // plans) and the issue-sync poll (through the Jira integration) need: none was registered here, so
        // those three jobs failed to construct on every run too. The filter provider has an empty whitelist —
        // no job takes a filter from a caller — because the API's lives in an assembly this host does not
        // reference.
        services.AddScoped<IEntitiesService, EntitiesService>();
        services.AddScoped<IRisksService, RisksService>();
        services.AddScoped<IMgmtReviewsService, MgmtReviewsService>();
        services.AddScoped<ServerServices.Filtering.IEntityFilterMapperProvider,
            ServerServices.Filtering.EmptyEntityFilterMapperProvider>();

    }
    
    public static void ConfigureHangFire(IServiceCollection services)
    {
        var sp = services.BuildServiceProvider();
        
        GlobalConfiguration.Configuration
            .UseActivator(new HangfireActivator(sp));

        JobStorage storage = new InMemoryStorage(new InMemoryStorageOptions
        {
            MaxExpirationTime = TimeSpan.FromHours(25), // Default value, we can also set it to `null` to disable.
            MaxStateHistoryLength = 100
        });
        
        
        var serverOptions = new BackgroundJobServerOptions()
        {
            WorkerCount = 2,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        try
        {
            StartHangFire(serverOptions, storage, sp);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error starting Hangfire Server...");
            
            Log.Error("Error starting Hangfire Server: {Message}", ex.Message);
        
            throw;
            
        }
        
    }
    private static void StartHangFire(BackgroundJobServerOptions serverOptions, JobStorage storage, IServiceProvider sp)
    {
        using var server = new BackgroundJobServer(serverOptions, storage);

        Console.WriteLine("Hangfire Server started...");

        JobStorage.Current = storage;

        BackgroundJob
            .Enqueue(() => Console.WriteLine("Testing job systems ..."));

        JobsManager.ConfigureScheduledJobs(sp);
        
        
        Console.CancelKeyPress += (sender, eArgs) => {
            AppManager.QuitEvent.Set();
            eArgs.Cancel = true;
        };


        AppManager.QuitEvent.WaitOne();
        
        /*while (true)
            {
                await Task.Delay(100);
            }*/
    }
    
}