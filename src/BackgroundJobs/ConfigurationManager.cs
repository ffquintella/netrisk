using System;
using System.Security.Claims;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Hangfire.LiteDB;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServerServices.Services;

namespace BackgroundJobs;

public static class ConfigurationManager
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration config, string logDir)
    {

        services.AddSingleton<IConfiguration>(config);

        
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
        
        services.AddSingleton<DALService>();
        services.AddSingleton<DatabaseService>();
        services.AddScoped<AuditCleanup>();
        services.AddScoped<ContributingImpactCalculation>();
        
        ConfigureHangFire(services);
        

        
    }
    
    public static void ConfigureHangFire(IServiceCollection services)
    {
        
        services.AddHangfire(configuration => configuration
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseLiteDbStorage()
            //.UseSQLiteStorage()
        );
        
        var sp = services.BuildServiceProvider();
        // Configure Hangfire here
        //GlobalConfiguration.Configuration.UseLiteDbStorage();
        //GlobalConfiguration.Configuration.UseSerilogLogProvider();
        //GlobalConfiguration.Configuration.UseActivator(new HangfireActivator(sp));
        
        
        GlobalConfiguration.Configuration
            .UseLiteDbStorage()
            //.UseSQLiteStorage()
            .UseActivator(new HangfireActivator(sp))
            .UseLogProvider(new HangFireLogProvider());
        
        
        
        //services.AddHangfire(x => x.UseLiteDbStorage());
        

        
        services.AddHangfireServer();
        
        var client = new BackgroundJobServer();
        // Only run once
        BackgroundJob
            .Enqueue(() => Console.WriteLine("Testing job systems ..."));

    }
}