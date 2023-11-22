using System;
using System.Security.Claims;
using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Hangfire.LiteDB;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServerServices.Services;
using ServerServices.Interfaces;

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
        services.AddScoped<IConfigurationsService, ConfigurationsService>();
        
        services.AddSingleton<DALService>();
        services.AddSingleton<DatabaseService>();
        
        //CLEANUP
        services.AddScoped<AuditCleanup>();
        services.AddScoped<BackupCleanup>();
        services.AddScoped<FileCleanup>();
        
        //CALCULATION
        services.AddScoped<ContributingImpactCalculation>();
        services.AddScoped<RiskScoreCalculation>();
        
        //BACKUP
        services.AddScoped<BackupWork>();
        
        ConfigureHangFire(services);
        

        
    }
    
    public static void ConfigureHangFire(IServiceCollection services)
    {
        var sp = services.BuildServiceProvider();


        GlobalConfiguration.Configuration
            .UseActivator(new HangfireActivator(sp));
        
        //JobStorage storage = new MemoryStorage(new MemoryStorageOptions());
        JobStorage storage = new LiteDbStorage("hangfire.db");
        var serverOptions = new BackgroundJobServerOptions()
        {
            WorkerCount = 2,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        using (var server = new BackgroundJobServer(serverOptions, storage))
        {
            Console.WriteLine("Hangfire Server started...");

            JobStorage.Current = storage;

            BackgroundJob
                .Enqueue(() => Console.WriteLine("Testing job systems ..."));
            
            JobsManager.ConfigureScheduledJobs();
            
            //Console.WriteLine("Stopping server...");
        }
        
        


    }
}