using System;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.Mock;

namespace ServerServices.Tests.DI;

public class ServiceRegistration
{
    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILogger>(new LoggerConfiguration().WriteTo.Console().CreateLogger());
        services.AddSingleton(MockDalService.Create());
        
        services.AddTransient<ICommentsService, CommentsService>();
        services.AddTransient<IClientRegistrationService, ClientRegistrationService>();

        return services.BuildServiceProvider();
    }
}