// See https://aka.ms/new-console-template for more information

using System.Runtime.InteropServices;
using BackgroundJobs;
using BackgroundJobs.Jobs;
using BackgroundJobs.Jobs.Calculation;
using Hangfire;
using Hangfire.Logging;
using Hangfire.Logging.LogProviders;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ServerServices.Interfaces;
using ServerServices.Services;
using Spectre.Console;
using ConfigurationManager = BackgroundJobs.ConfigurationManager;
using ILogger = Serilog.ILogger;


// Track 7 finding NR-2026-033. Two things were wrong with the previous builder.
//
// It never called AddEnvironmentVariables, so there was no way at all to supply a secret except by
// writing it into appsettings.json on the target host — which is exactly what milestone 7.3.3
// forbids, and the reason the Puppet templates render the database password to disk (NR-2026-025).
// With this provider in place, Database__ConnectionString and https__certificate__password work as
// documented in docs/security/SECRETS.md, with no other change.
//
// And the order was inverted: appsettings.json was added *after* user-secrets, so the committed file
// won every key the two had in common. That is the opposite of what a developer expects, and the
// kind of thing that is only noticed when an override silently does nothing. Later providers win in
// .NET configuration, so the correct order is file, then developer overrides, then environment.
var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json");

#if DEBUG
configurationBuilder.AddUserSecrets<Program>();
#endif

var configuration = configurationBuilder.AddEnvironmentVariables();

var config = configuration.Build();


string logDir = "";
if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/netrisk-background-jobs";
if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    logDir = Path.Combine( "/var/log/" , "netrisk");
if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    logDir = Path.Combine( "/tmp/" , "netrisk-background-jobs");
Directory.CreateDirectory(logDir);

var logFile = Path.Combine(logDir, "nr-background-jobs.log");

Logger? logger;
LoggingLevelSwitch defaultLoggingLevel = new LoggingLevelSwitch();

defaultLoggingLevel.MinimumLevel = LogEventLevel.Information;

logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(defaultLoggingLevel)
    .MinimumLevel.Override("Microsoft", defaultLoggingLevel)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", defaultLoggingLevel)
    .MinimumLevel.Override("Pomelo.EntityFrameworkCore", defaultLoggingLevel)
    .WriteTo.Console()
    .WriteTo.File(logFile, fileSizeLimitBytes: 1000000, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
    .CreateLogger();

Log.Logger = logger;

AnsiConsole.MarkupLine("[bold]Starting[/] background jobs...");


var services = new ServiceCollection();

services.AddSingleton<ILogger>(logger);

var factory = new SerilogLoggerFactory(logger);
services.AddSingleton<ILoggerFactory>(factory);

ConfigurationManager.ConfigureServices(services, config, logDir);

