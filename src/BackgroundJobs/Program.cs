// See https://aka.ms/new-console-template for more information

using System.Runtime.InteropServices;
using BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Spectre.Console;
using ConfigurationManager = BackgroundJobs.ConfigurationManager;


#if DEBUG
var configuration =  new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>()
    .AddJsonFile($"appsettings.json");
#else 
var configuration =  new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile($"appsettings.json");
#endif

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
    .WriteTo.File(logFile, fileSizeLimitBytes: 10000)
    .CreateLogger();

Log.Logger = logger;

AnsiConsole.MarkupLine("[bold]Starting[/] background jobs...");


var services = new ServiceCollection();

services.AddSingleton<ILogger>(logger);

ConfigurationManager.ConfigureServices(services, config, logDir);

Console.CancelKeyPress += (sender, eArgs) => {
    AppManager.QuitEvent.Set();
    eArgs.Cancel = true;
};


JobsManager.ConfigureScheduledJobs();



AppManager.QuitEvent.WaitOne();