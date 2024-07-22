using System;
using System.Security.Claims;
using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.LiteDB;
using Hangfire.MemoryStorage;
using LiteDB;
using LiteDB.Engine;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Moq;
using Serilog;
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
        
        services.AddSingleton<DalService>();
        services.AddSingleton<DatabaseService>();
        
        services.AddSingleton<IDalService, DalService>();
        services.AddSingleton<IDatabaseService, DatabaseService>();
        
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
    
    public static async void ConfigureHangFire(IServiceCollection services)
    {
        var sp = services.BuildServiceProvider();
        

        GlobalConfiguration.Configuration
            .UseActivator(new HangfireActivator(sp));

        //JobStorage storage = new MemoryStorage(new MemoryStorageOptions());
        //JobStorage storage = new LiteDbStorage("hangfire.db");
        
        
        JobStorage storage = new InMemoryStorage(new InMemoryStorageOptions
        {
            MaxExpirationTime = TimeSpan.FromHours(25), // Default value, we can also set it to `null` to disable.
            MaxStateHistoryLength = 100
        });
        
        
        var serverOptions = new BackgroundJobServerOptions()
        {
            WorkerCount = 2,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        try
        {
            StartHangFire(serverOptions, storage);
            
        }catch (LiteException ex)
        {
            if (ex.Message.Contains("empty page must be defined as empty type"))
            {
                File.Delete("hangfire.db");
                StartHangFire(serverOptions, storage);
            }
            else
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error starting Hangfire Server...");
            
            Log.Error("Error starting Hangfire Server: {Message}", ex.Message);
        
            throw;
            
        }
        
    }
    private static void StartHangFire(BackgroundJobServerOptions serverOptions, JobStorage storage)
    {
        using var server = new BackgroundJobServer(serverOptions, storage);
        
        Console.WriteLine("Hangfire Server started...");

        JobStorage.Current = storage;
        
        BackgroundJob
            .Enqueue(() => Console.WriteLine("Testing job systems ..."));

        JobsManager.ConfigureScheduledJobs();
        
        
        Console.CancelKeyPress += (sender, eArgs) => {
            AppManager.QuitEvent.Set();
            eArgs.Cancel = true;
        };


        AppManager.QuitEvent.WaitOne();
        
        /*while (true)
            {
                await Task.Delay(100);
            }*/
    }
    
}