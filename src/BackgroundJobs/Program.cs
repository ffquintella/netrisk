// See https://aka.ms/new-console-template for more information

using System.Runtime.InteropServices;
using BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    logDir =  "/var/log/netrisk";
}
else
{
    logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/NRBackgroundJobs";
}

Directory.CreateDirectory(logDir);


AnsiConsole.MarkupLine("[bold]Starting[/] background jobs...");


var services = new ServiceCollection();
ConfigurationManager.ConfigureServices(services, config, logDir);

Console.CancelKeyPress += (sender, eArgs) => {
    AppManager.QuitEvent.Set();
    eArgs.Cancel = true;
};

ConfigurationManager.ConfigureHangFire(services);
JobsManager.ConfigureScheduledJobs();



AppManager.QuitEvent.WaitOne();