// See https://aka.ms/new-console-template for more information

using System;
using ConsoleClient.Commands;
using DAL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using ServerServices;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using System.IO;
using ServerServices.Interfaces;
using ServerServices.Services;


var configuration =  new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddUserSecrets<Program>()
    .AddJsonFile($"appsettings.json");
var config = configuration.Build();


var logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/SRConsoleClient";

Directory.CreateDirectory(logDir);

var logPath = logDir + "/logs";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Spectre("{Timestamp:HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}", LogEventLevel.Warning)
    .WriteTo.RollingFile(logPath, outputTemplate: "{Timestamp:dd/MM/yy HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: LogEventLevel.Debug)
    .MinimumLevel.Verbose()
    .CreateLogger();

#if DEBUG
Log.Information("Starting Console Client with debug");
#endif

var services = new ServiceCollection();
// add extra services to the container here
services.AddSingleton<Serilog.ILogger>(Log.Logger);
services.AddSingleton<IConfiguration>(config);
services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
services.AddSingleton<DALManager>();

var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
#if DEBUG
    config.PropagateExceptions();
    config.ValidateExamples();
#endif
    
    //config.AddCommand<SelfTestCommand>("selfTest");
    config.AddCommand<RegistrationCommand>("registration");

});

return app.Run(args);
