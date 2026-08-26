using System.Runtime.InteropServices;
using System.Security.Claims;
using ConsoleClient.Commands;
using DAL.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.Spectre;
using ServerServices.Interfaces;
using ServerServices.SchemaUpgrade;
using ServerServices.Findings;
using ServerServices.Importers;
using ServerServices.Importers.Dedup;
using ServerServices.Governance;
using ServerServices.Integrations;
using ServerServices.Services;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

namespace ConsoleClient;

public class Program
{
    public static void Main(string[] args)
    {
        RunApp(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
#if DEBUG
                config.AddUserSecrets<Program>();
#endif
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                // Track 7 finding NR-2026-033: CreateDefaultBuilder already adds the environment
                // provider, but AddJsonFile above was registered after it, so the committed file won
                // every key they shared. Re-adding it last restores the documented precedence — file,
                // then developer overrides, then environment (docs/security/SECRETS.md).
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                string logDir = "";
                string logPath = "";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    logDir = "/var/log/netrisk";
                    logPath = logDir + "/logs";
                }
                else
                {
                    logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) +
                             "/NRConsoleClient";
                    logPath = logDir + "/logs";
                }

                Directory.CreateDirectory(logDir);

                Log.Logger = new LoggerConfiguration()
                    .WriteTo.Spectre("{Timestamp:HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}",
                        LogEventLevel.Warning)
                    .WriteTo.File(logPath,
                        outputTemplate: "{Timestamp:dd/MM/yy HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}",
                        restrictedToMinimumLevel: LogEventLevel.Debug)
                    .MinimumLevel.Verbose()
                    .CreateLogger();

#if DEBUG
                Log.Information("Starting Console Client with debug");
#endif

                services.AddSingleton(Log.Logger);
                services.AddSingleton<IConfiguration>(configuration);
                
                var httpAccessor = new Mock<IHttpContextAccessor>();
                var httpContext = new DefaultHttpContext();

                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Sid, "1"),
                    new Claim(ClaimTypes.Name, "BackgroundServices"),
                }, "mock"));

                httpAccessor.SetupGet(acessor => acessor.HttpContext)
                    .Returns(httpContext);

                services.AddSingleton<IHttpContextAccessor>(provider => httpAccessor.Object);
                
                services.AddSingleton<IDalService, DalService>();
                services.AddSingleton<IRiskCalculationService, RiskCalculationService>();
                services.AddSingleton<IEnvironmentService, EnvironmentService>();
                services.AddSingleton<ISyncKeyService, SyncKeyService>();
                services.AddSingleton<ISyncClient, SyncClient>();
                
                
                services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
                services.AddScoped<IDatabaseService, DatabaseService>();
                services.AddScoped<ISchemaUpgradeService, SchemaUpgradeService>();
                services.AddScoped<IUsersService, UsersService>();
                services.AddScoped<IRolesService, RolesService>();
                services.AddScoped<ISettingsService, SettingsService>();
                services.AddScoped<ITechnologiesService, TechnologiesService>();
                services.AddScoped<IPermissionsService, PermissionsService>();
                services.AddScoped<IConfigurationsService, ConfigurationsService>();

                // Track 3 (ASPM). The CI gate reads an import's counts, which means the ingestion
                // service; that in turn needs the dedup engine and the SLA policy resolver.
                services.AddScoped<IPluginsService, PluginsService>();
                services.AddScoped<IDeduplicationService, DeduplicationService>();
                services.AddScoped<ISlaService, SlaService>();
                services.AddScoped<IFindingIngestionService, FindingIngestionService>();
                services.AddScoped<IFindingLifecycleService, FindingLifecycleService>();

                // Track 4 (Integrations). The console needs the graph because the lifecycle and
                // ingestion services now raise notification events, and because `integrations`
                // commands drive the connections directly.
                services.AddTrack4Integrations();

                // Track 8 (Risk governance). The console's schema-upgrade and reporting commands read
                // the governance tables, and `calculation` now drives the residual pass.
                services.AddTrack8Governance();

                var factory = new SerilogLoggerFactory(Log.Logger);
                services.AddSingleton<ILoggerFactory>(factory);

                // AutoMapper profiles removed; replaced by Mapster or direct Adapt usage

                var dalService = new DalService(configuration, new Mock<IHttpContextAccessor>().Object);

                services.AddDbContext<NRDbContext>(options =>
                {
                    options.UseMySql(dalService.GetConnectionString(), dalService.GetMysqlServerVersion());
                    options.UseLoggerFactory(factory);
                });



                var registrar = new DependencyInjectionRegistrar(services);
                var app = new CommandApp<RegistrationCommand>(registrar);

                app.Configure(config =>
                {
#if DEBUG
                    config.PropagateExceptions();
                    config.ValidateExamples();
#endif

                    config.AddCommand<UserCommand>("user");
                    config.AddCommand<RegistrationCommand>("registration");
                    config.AddCommand<DatabaseCommand>("database");
                    config.AddCommand<SettingsCommand>("settings");
                    config.AddCommand<TechnologyCommand>("technologies");
                    config.AddCommand<CalculationCommands>("calculation");
                    config.AddCommand<KeysCommand>("keys");
                    config.AddCommand<WebsiteCommand>("website");
                    config.AddCommand<CiCommand>("ci");
                });

                services.AddSingleton(app);
            })
            .UseSerilog();

    public static void RunApp(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        var app = host.Services.GetRequiredService<CommandApp<RegistrationCommand>>();
        app.Run(args);
    }
}