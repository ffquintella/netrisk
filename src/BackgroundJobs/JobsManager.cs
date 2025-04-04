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
            .AddOrUpdate<MessageCleanup>("MessageCleanup",
                x => x.Run(), Cron.Daily(2));
        RecurringJob
            .AddOrUpdate<TmpCleanup>("TmpCleanup",
                x => x.Run(), Cron.Daily(3));
        RecurringJob
            .AddOrUpdate<BackupCleanup>("BackupCleanup",
                x => x.Run(), Cron.Daily(4));

    }

    private static void ConfigureCalculationJobs()
    {
        
         RecurringJob
            .AddOrUpdate<ContributingImpactCalculation>("ContributingImpactCalculation",
                x => x.Run(), @"*/10 * * * *"); 
         
         RecurringJob
             .AddOrUpdate<RiskScoreCalculation>("RiskScoreCalculation",
                 x => x.Run(), @"0 */2 * * *"); 
         
         //DEBUG
         //RecurringJob.AddOrUpdate<BackupWork>("DebugService", x => x.Run(), @"*/1 * * * *"); 
            
    }
}