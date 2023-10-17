using Hangfire;
using Hangfire.LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BackgroundJobs;

public static class ConfigurationManager
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILogger>(Log.Logger);
    }
    
    public static void ConfigureHangFire()
    {
        // Configure Hangfire here
        GlobalConfiguration.Configuration.UseLiteDbStorage();
        var client = new BackgroundJobServer();
        // Only run once
        BackgroundJob
            .Enqueue(() => Console.WriteLine("Testing job systems ..."));
    }
}