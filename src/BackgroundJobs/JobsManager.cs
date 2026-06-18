//using BackgroundJobs.Jobs.Backup;

using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using BackgroundJobs.Jobs.Sync;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;

namespace BackgroundJobs;

public static class JobsManager
{
    public static void ConfigureScheduledJobs(IServiceProvider sp)
    {
        ConfigureBackupJobs();
        ConfigureCleanupJobs();
        ConfigureCalculationJobs();
        ConfigureSyncJobs(sp);
    }

    private static void ConfigureSyncJobs(IServiceProvider sp)
    {
        var settings = sp.GetService<ISettingsService>();

        var bulkMinutes = WebsiteSyncSettings.DefaultIntervalMinutes;
        var fastMinutes = WebsiteSyncSettings.DefaultFastIntervalMinutes;

        if (settings != null)
        {
            bulkMinutes = ReadMinutes(settings, WebsiteSyncSettings.IntervalKey, bulkMinutes);
            fastMinutes = ReadMinutes(settings, WebsiteSyncSettings.FastIntervalKey, fastMinutes);
        }

        RecurringJob.AddOrUpdate<SyncBulkJob>("WebsiteSyncBulk",
            x => x.Run(), WebsiteSyncSettings.MinutesToCron(bulkMinutes));
        RecurringJob.AddOrUpdate<SyncFastJob>("WebsiteSyncFast",
            x => x.Run(), WebsiteSyncSettings.MinutesToCron(fastMinutes));
    }

    private static int ReadMinutes(ISettingsService settings, string key, int fallback)
    {
        var raw = WebsiteSyncSettings.GetValueAsync(settings, key).GetAwaiter().GetResult();
        return int.TryParse(raw, out var minutes) && minutes >= 1 ? minutes : fallback;
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
            .AddOrUpdate<TransactionsCleanup>("TransactionsCleanup",
                x => x.Run(), Cron.Minutely()); 
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
        RecurringJob
            .AddOrUpdate<BiometricTransactionCleanup>("BiometricTransactionCleanup",
                x => x.Run(), Cron.Daily(5)); 

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