//using BackgroundJobs.Jobs.Backup;

using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using BackgroundJobs.Jobs.Findings;
using BackgroundJobs.Jobs.Governance;
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
        ConfigureGovernanceJobs();
    }

    /// <summary>
    /// Track 8 automation.
    ///
    /// The order in the day is the dependency order, and it matters. The residual pass runs at 02:20,
    /// twenty minutes after the two-hourly score calculation it depends on — running it at 02:00 would
    /// have it racing the pass whose output it reads. Risk-acceptance expiry runs at 06:15 —
    /// alongside the finding-level pass at 06:00, not before it, so the two never contend on
    /// `risk_acceptances`. The cadence sweep runs at 07:30, after both expiry passes, so a risk whose
    /// acceptance lapsed overnight appears in this morning's overdue-review notification rather than
    /// tomorrow's. Campaign generation runs last, at 08:00, so a newly reopened risk is in the
    /// campaign it belongs to.
    ///
    /// Retention runs at 02:30, in the quiet hour, because it is the only job here that deletes.
    /// </summary>
    private static void ConfigureGovernanceJobs()
    {
        RecurringJob
            .AddOrUpdate<ResidualRiskCalculation>("ResidualRiskCalculation",
                x => x.Run(), "20 2 * * *");

        RecurringJob
            .AddOrUpdate<GovernanceRetentionJob>("GovernanceRetention",
                x => x.Run(), "30 2 * * *");

        RecurringJob
            .AddOrUpdate<RiskAcceptanceExpiryPass>("RiskLevelAcceptanceExpiry",
                x => x.Run(), "15 6 * * *");

        RecurringJob
            .AddOrUpdate<RiskReviewCadenceJob>("RiskReviewCadence",
                x => x.Run(), "30 7 * * *");

        RecurringJob
            .AddOrUpdate<RiskReviewCampaignJob>("RiskReviewCampaigns",
                x => x.Run(), Cron.Daily(8));
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