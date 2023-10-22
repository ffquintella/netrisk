using System.Security.Claims;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Hangfire.LiteDB;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using ServerServices.Services;

namespace BackgroundJobs;

public static class ConfigurationManager
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration config, string logDir)
    {
        
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Spectre("{Timestamp:HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}", LogEventLevel.Warning)
            .WriteTo.File(logDir, outputTemplate: "{Timestamp:dd/MM/yy HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: LogEventLevel.Warning)
            .MinimumLevel.Verbose()
            .CreateLogger();
        
        services.AddSingleton<ILogger>(Log.Logger);
        services.AddSingleton<IConfiguration>(config);
        services.AddHangfire(x => x.UseLiteDbStorage());
        services.AddHangfireServer();
        
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
        services.AddScoped<AuditCleanup>();
        services.AddScoped<ContributingImpactCalculation>();
        
    }
    
    public static void ConfigureHangFire(IServiceCollection services)
    {
        
        var sp = services.BuildServiceProvider();
        // Configure Hangfire here
        GlobalConfiguration.Configuration.UseLiteDbStorage();
        GlobalConfiguration.Configuration.UseSerilogLogProvider();
        GlobalConfiguration.Configuration.UseActivator(new HangfireActivator(sp));
        
        
        var client = new BackgroundJobServer();
        // Only run once
        BackgroundJob
            .Enqueue(() => Console.WriteLine("Testing job systems ..."));

    }
}