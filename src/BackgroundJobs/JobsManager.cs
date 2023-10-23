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
        ConfigureCalculationJobs();

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
        //BackgroundJob
        //    .Enqueue<ContributingImpactCalculation>(x=> x.Run());
        
         RecurringJob
            .AddOrUpdate<ContributingImpactCalculation>("ContributingImpactCalculation",
                x => x.Run(), @"*/10 * * * *"); 
         
         RecurringJob
             .AddOrUpdate<RiskScoreCalculation>("RiskScoreCalculation",
                 x => x.Run(), @"0 */2 * * *"); 
            
    }
}