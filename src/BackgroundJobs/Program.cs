// See https://aka.ms/new-console-template for more information

using BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;



AnsiConsole.MarkupLine("[bold]Starting[/] background jobs...");


var services = new ServiceCollection();
ConfigurationManager.ConfigureServices(services);

Console.CancelKeyPress += (sender, eArgs) => {
    AppManager.QuitEvent.Set();
    eArgs.Cancel = true;
};


ConfigurationManager.ConfigureHangFire();



AppManager.QuitEvent.WaitOne();