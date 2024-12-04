using System;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.EntitiesDto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Model.DTO;
using NSubstitute;
using RestSharp;
using Serilog;
using Serilog.Extensions.Logging;
using Splat;
using ILogger = Serilog.ILogger;

namespace ClientServices.Tests.DI;

public class ServiceRegistration
{
    public static IServiceProvider GetServiceProvider()
    {

        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        
        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;
        
        var splat = Locator.CurrentMutable;
        
        var services = new ServiceCollection();
        
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());
        
        splat.RegisterLazySingleton<ILoggerFactory>(() => factory);
        splat.RegisterLazySingleton<ILogger>(() => new LoggerConfiguration().WriteTo.Console().CreateLogger());
        
        var mockClient = MockSetup.GetRestClient();
        services.AddSingleton<IRestClient>(mockClient);
        //splat.RegisterLazySingleton<IRestClient>(() => mockClient);
        
        services.AddSingleton<IRestService>(MockSetup.GetRestService());
        services.AddTransient<IHostsService, HostsRestService>();
        
        services.AddTransient<ICommentsService, CommentsRestService>();
        services.AddTransient<IIncidentResponsePlansService, IncidentResponsePlansRestService>();
        services.AddTransient<IRisksService, RisksRestService>();
        services.AddTransient<IAuthenticationService, AuthenticationRestService>();
        services.AddTransient<IRegistrationService, RegistrationService>();
        services.AddTransient<IMutableConfigurationService, MutableConfigurationService>();
        services.AddSingleton<IEnvironmentService>(new EnvironmentService("production"));
        

        return services.BuildServiceProvider();
    }
}