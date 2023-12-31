//using BackgroundJobs.Jobs.Backup;

using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs;

public static class JobsManager
{
    public static void ConfigureScheduledJobs()
    {
        ConfigureBackupJobs();
        ConfigureCleanupJobs();
        ConfigureCalculationJobs();
    }

    private static void ConfigureBackupJobs()
    {
        RecurringJob
            .AddOrUpdate<BackupWork>("BackupWork",
                x => x.Run(), Cron.Daily(19)); 
        
        //RecurringJob
        //    .AddOrUpdate<BackupWork>(x => x.Run(), Cron.Minutely); 
    }

    private static void ConfigureCleanupJobs()
    {
        RecurringJob
            .AddOrUpdate<AuditCleanup>("AuditCleanup",
                x => x.Run(), Cron.Daily(23)); 
        RecurringJob
            .AddOrUpdate<FileCleanup>("FileCleanup",
                x => x.Run(), Cron.Daily(1));
        RecurringJob
            .AddOrUpdate<BackupCleanup>("BackupCleanup",
                x => x.Run(), Cron.Daily(4));
        
        //BackgroundJob
        //    .Enqueue<AuditCleanup>(x=> x.Run());
    }

    private static void ConfigureCalculationJobs()
    {
        
         RecurringJob
            .AddOrUpdate<ContributingImpactCalculation>("ContributingImpactCalculation",
                x => x.Run(), @"*/10 * * * *"); 
         
         RecurringJob
             .AddOrUpdate<RiskScoreCalculation>("RiskScoreCalculation",
                 x => x.Run(), @"0 */2 * * *"); 
            
    }
}