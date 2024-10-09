// See https://aka.ms/new-console-template for more information

using System;
using System.Diagnostics;
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
using System.Runtime.InteropServices;
using System.Security.Claims;
using ConsoleClient.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog.Extensions.Logging;
using ServerServices.ClassMapping;
using ServerServices.Interfaces;
using ServerServices.Services;

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
string logPath = "";

if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    logDir =  "/var/log/netrisk";
    logPath = logDir + "/logs";
}
else
{
    logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/NRConsoleClient";
    logPath = logDir + "/logs";
}

Directory.CreateDirectory(logDir);



Log.Logger = new LoggerConfiguration()
    .WriteTo.Spectre("{Timestamp:HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}", LogEventLevel.Warning)
    .WriteTo.RollingFile(logPath, outputTemplate: "{Timestamp:dd/MM/yy HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: LogEventLevel.Debug)
    .MinimumLevel.Verbose()
    .CreateLogger();

#if DEBUG
Log.Information("Starting Console Client with debug");
//DiagnosticListener.AllListeners.Subscribe(new DiagnosticObserver());
#endif

var services = new ServiceCollection();
// add extra services to the container here
services.AddSingleton<Serilog.ILogger>(Log.Logger);
services.AddSingleton<IConfiguration>(config);
services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
services.AddSingleton<IDalService, DalService>();
services.AddScoped<IDatabaseService, DatabaseService>();
services.AddScoped<IUsersService, UsersService>();
services.AddScoped<IRolesService, RolesService>();
services.AddScoped<ISettingsService, SettingsService>();
services.AddScoped<IPermissionsService, PermissionsService>();
services.AddScoped<IConfigurationsService, ConfigurationsService>();

var factory = new SerilogLoggerFactory(Log.Logger);
services.AddSingleton<ILoggerFactory>(factory);

services.AddAutoMapper(typeof(ClientProfile));
services.AddAutoMapper(typeof(ObjectUpdateProfile));
services.AddAutoMapper(typeof(UserProfile));
services.AddAutoMapper(typeof(EntityProfile));
services.AddAutoMapper(typeof(MgmtReviewProfile));

var httpAccessor = new Mock<IHttpContextAccessor>();
var httpContext = new DefaultHttpContext();
        
httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
{
    new Claim(ClaimTypes.Sid, "1"),
    new Claim(ClaimTypes.Name, "BackgroundServices"),
}, "mock"));

httpAccessor.SetupGet(acessor => acessor.HttpContext)
    .Returns(httpContext);

services.AddScoped<IHttpContextAccessor>(provider => httpAccessor.Object);

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

});

return app.Run(args);
