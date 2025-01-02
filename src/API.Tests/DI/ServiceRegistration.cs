using System;
using API.Controllers;
using API.Tests.Mock;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace API.Tests.DI;

public static class ServiceRegistration
{
    public static IServiceProvider GetServiceProvider()
    {
        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        
        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;
        
        var services = new ServiceCollection();
        
        
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());
        
        services.AddSingleton(MockedHttpContextAccessor.Create());
        services.AddSingleton(MockedUsersService.Create());
        services.AddSingleton(MockedIncidentResponsePlansService.Create());
        services.AddSingleton(MockedMitigationsService.Create());
        services.AddSingleton(MockedFilesService.Create());
        services.AddSingleton(MockedMgmtReviewsService.Create());
        services.AddSingleton(MockedRisksService.Create());
        services.AddSingleton(MockedIncidentsService.Create());
        
        services.AddTransient<IncidentResponsePlansController>();
        services.AddTransient<RisksController>();
        
        
        return services.BuildServiceProvider();
    }
}