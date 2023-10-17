using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs;

public static class JobsManager
{
    public static void ConfigureScheduledJobs()
    {
        
        ConfigureCleanupJobs();

    }


    private static void ConfigureCleanupJobs()
    {
        RecurringJob
            .AddOrUpdate<AuditCleanup>("AuditCleanup",
                x => x.Run(), Cron.Daily(23)); 
        //BackgroundJob
        //    .Enqueue<AuditCleanup>(x=> x.Run());
    }

    private static void ConfigureCalculationJobs()
    {
        RecurringJob
            .AddOrUpdate<ContributingImpactCalculation>("ContributingImpactCalculation",
                x => x.Run(), "*/10 * * * *"); // every 10 minutes
    }
}