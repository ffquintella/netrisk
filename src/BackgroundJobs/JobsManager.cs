//using BackgroundJobs.Jobs.Backup;

using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using BackgroundJobs.Jobs.Findings;
using BackgroundJobs.Jobs.Integrations;
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
        ConfigureFindingJobs();
        ConfigureIntegrationJobs();
    }

    /// <summary>
    /// Track 4 automation.
    ///
    /// The notification sweep runs every minute: an immediate send is attempted inline when an event is
    /// raised, so this is the retry and digest pass, and a minute is the resolution at which a digest
    /// window is a window rather than a delay.
    ///
    /// The posture syncs run at 03:00 and 04:00 — before the risk-acceptance expiry pass at 06:00 and
    /// the SLA digest at 07:00, so a finding Vision One reported overnight appears in the right person's
    /// digest the same morning instead of tomorrow's.
    /// </summary>
    private static void ConfigureIntegrationJobs()
    {
        RecurringJob
            .AddOrUpdate<NotificationDispatchJob>("NotificationDispatch",
                x => x.Run(), Cron.Minutely());

        // Five minutes rather than minutely: the webhook is the primary path, and every poll is a
        // request per link against somebody's Jira.
        RecurringJob
            .AddOrUpdate<IssueSyncPollingJob>("IssueSyncPolling",
                x => x.Run(), @"*/5 * * * *");

        RecurringJob
            .AddOrUpdate<TrendMicroSyncJob>("TrendMicroSync",
                x => x.Run(), Cron.Daily(3));

        RecurringJob
            .AddOrUpdate<SecurityScorecardSyncJob>("SecurityScorecardSync",
                x => x.Run(), Cron.Daily(4));
    }

    /// <summary>
    /// Track 3 (ASPM) automation. Both run daily and early: the expiry pass reopens findings whose
    /// acceptance lapsed overnight, and the SLA digest has to run after it so a finding that came
    /// back today appears in the right person's digest the same morning rather than tomorrow's.
    /// </summary>
    private static void ConfigureFindingJobs()
    {
        RecurringJob
            .AddOrUpdate<RiskAcceptanceExpiryJob>("RiskAcceptanceExpiry",
                x => x.Run(), Cron.Daily(6));

        RecurringJob
            .AddOrUpdate<SlaBreachNotificationJob>("SlaBreachNotification",
                x => x.Run(), Cron.Daily(7));
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