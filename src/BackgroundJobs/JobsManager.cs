//using BackgroundJobs.Jobs.Backup;

using BackgroundJobs.Jobs.Backup;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Cleanup;
using BackgroundJobs.Jobs.Findings;
using BackgroundJobs.Jobs.Governance;
using BackgroundJobs.Jobs.Integrations;
using BackgroundJobs.Jobs.Sync;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using ServerServices.Interfaces;

namespace BackgroundJobs;

public static class JobsManager
{
    public static void ConfigureScheduledJobs(IServiceProvider sp) =>
        ConfigureScheduledJobs(sp, new RecurringJobManager());

    /// <summary>
    /// Schedules every recurring job on <paramref name="jobs"/>. Production passes a manager over
    /// <c>JobStorage.Current</c>; <c>RecurringJobsContainerTest</c> passes a recording one, so the list of
    /// scheduled job types it resolves from the container is this method's list rather than a copy of it
    /// (T237, S39).
    /// </summary>
    public static void ConfigureScheduledJobs(IServiceProvider sp, IRecurringJobManager jobs)
    {
        ConfigureBackupJobs(jobs);
        ConfigureCleanupJobs(jobs);
        ConfigureCalculationJobs(jobs);
        ConfigureSyncJobs(sp, jobs);
        ConfigureFindingJobs(jobs);
        ConfigureIntegrationJobs(jobs);
        ConfigureGovernanceJobs(jobs);
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
    private static void ConfigureGovernanceJobs(IRecurringJobManager jobs)
    {
        jobs
            .AddOrUpdate<ResidualRiskCalculation>("ResidualRiskCalculation",
                x => x.Run(), "20 2 * * *");

        jobs
            .AddOrUpdate<GovernanceRetentionJob>("GovernanceRetention",
                x => x.Run(), "30 2 * * *");

        // Stage 9.5 (S46 §4.6): the derived flags after the KEV/EPSS syncs (05:00, 05:20) and before the
        // expiry passes, so a Gate A onset is escalated before anything reads the register that morning.
        jobs
            .AddOrUpdate<RiskFlagsDerivationJob>(RiskFlagsDerivationJob.JobId,
                x => x.Run(), RiskFlagsDerivationJob.Cron);

        jobs
            .AddOrUpdate<RiskAcceptanceExpiryPass>("RiskLevelAcceptanceExpiry",
                x => x.Run(), "15 6 * * *");

        // Stage 9.8 (S49 §4.10): every KRI, after the expiry passes and before the cadence sweep, so a breach seen
        // overnight has flagged its risks for review by the time the 07:30 message goes out.
        jobs
            .AddOrUpdate<KriEvaluationJob>(KriEvaluationJob.JobId,
                x => x.Run(), KriEvaluationJob.Cron);

        // Stage 9.9 (S50 §4.2): the archives due for their quarterly review, after the KRI evaluation (which may reopen
        // one) and before the cadence sweep.
        jobs
            .AddOrUpdate<RiskArchiveReviewJob>(RiskArchiveReviewJob.JobId,
                x => x.Run(), RiskArchiveReviewJob.Cron);

        jobs
            .AddOrUpdate<RiskReviewCadenceJob>("RiskReviewCadence",
                x => x.Run(), RiskReviewCadenceJob.Cron);

        jobs
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
    private static void ConfigureIntegrationJobs(IRecurringJobManager jobs)
    {
        jobs
            .AddOrUpdate<NotificationDispatchJob>("NotificationDispatch",
                x => x.Run(), Cron.Minutely());

        // Five minutes rather than minutely: the webhook is the primary path, and every poll is a
        // request per link against somebody's Jira.
        jobs
            .AddOrUpdate<IssueSyncPollingJob>("IssueSyncPolling",
                x => x.Run(), @"*/5 * * * *");

        jobs
            .AddOrUpdate<TrendMicroSyncJob>("TrendMicroSync",
                x => x.Run(), Cron.Daily(3));

        jobs
            .AddOrUpdate<SecurityScorecardSyncJob>("SecurityScorecardSync",
                x => x.Run(), Cron.Daily(4));

        // Hourly, at :10 so it never starts in the same minute as the syncs above. A run left
        // Running by a killed process both misreports as live work and refuses the next sync of that
        // connection, so the horizon that matters is "how long until an operator can retry", not
        // "how long until the display is tidy".
        jobs
            .AddOrUpdate<IntegrationSyncReaperJob>("IntegrationSyncReaper",
                x => x.Run(), "10 * * * *");

        // Track 9 Stage 9.4 (S45 §6) — the public exploitation-signal feeds. At 05:00 and 05:20: after the
        // posture syncs above, so a finding imported overnight gets its KEV status and EPSS the same night,
        // and before the 06:00 expiry pass and the 07:00 SLA digest. The two never start in the same minute.
        jobs
            .AddOrUpdate<KevCatalogueSyncJob>(KevCatalogueSyncJob.JobId,
                x => x.Run(), KevCatalogueSyncJob.Cron);

        jobs
            .AddOrUpdate<EpssSyncJob>(EpssSyncJob.JobId,
                x => x.Run(), EpssSyncJob.Cron);
    }

    /// <summary>
    /// Track 3 (ASPM) automation. Both run daily and early: the expiry pass reopens findings whose
    /// acceptance lapsed overnight, and the SLA digest has to run after it so a finding that came
    /// back today appears in the right person's digest the same morning rather than tomorrow's.
    /// </summary>
    private static void ConfigureFindingJobs(IRecurringJobManager jobs)
    {
        jobs
            .AddOrUpdate<RiskAcceptanceExpiryJob>("RiskAcceptanceExpiry",
                x => x.Run(), Cron.Daily(6));

        jobs
            .AddOrUpdate<SlaBreachNotificationJob>("SlaBreachNotification",
                x => x.Run(), Cron.Daily(7));
    }

    private static void ConfigureSyncJobs(IServiceProvider sp, IRecurringJobManager jobs)
    {
        var settings = sp.GetService<ISettingsService>();

        var bulkMinutes = WebsiteSyncSettings.DefaultIntervalMinutes;
        var fastMinutes = WebsiteSyncSettings.DefaultFastIntervalMinutes;

        if (settings != null)
        {
            bulkMinutes = ReadMinutes(settings, WebsiteSyncSettings.IntervalKey, bulkMinutes);
            fastMinutes = ReadMinutes(settings, WebsiteSyncSettings.FastIntervalKey, fastMinutes);
        }

        jobs.AddOrUpdate<SyncBulkJob>("WebsiteSyncBulk",
            x => x.Run(), WebsiteSyncSettings.MinutesToCron(bulkMinutes));
        jobs.AddOrUpdate<SyncFastJob>("WebsiteSyncFast",
            x => x.Run(), WebsiteSyncSettings.MinutesToCron(fastMinutes));
    }

    private static int ReadMinutes(ISettingsService settings, string key, int fallback)
    {
        var raw = WebsiteSyncSettings.GetValueAsync(settings, key).GetAwaiter().GetResult();
        return int.TryParse(raw, out var minutes) && minutes >= 1 ? minutes : fallback;
    }

    private static void ConfigureBackupJobs(IRecurringJobManager jobs)
    {
        jobs
            .AddOrUpdate<BackupWork>("BackupWork",
                x => x.Run(), Cron.Daily(19)); 
        
        //RecurringJob
        //    .AddOrUpdate<BackupWork>(x => x.Run(), Cron.Minutely); 
    }

    private static void ConfigureCleanupJobs(IRecurringJobManager jobs)
    {
        
        jobs
            .AddOrUpdate<TransactionsCleanup>("TransactionsCleanup",
                x => x.Run(), Cron.Minutely()); 
        jobs
            .AddOrUpdate<AuditCleanup>("AuditCleanup",
                x => x.Run(), Cron.Daily(23)); 
        jobs
            .AddOrUpdate<FileCleanup>("FileCleanup",
                x => x.Run(), Cron.Daily(1));
        jobs
            .AddOrUpdate<MessageCleanup>("MessageCleanup",
                x => x.Run(), Cron.Daily(2));
        jobs
            .AddOrUpdate<TmpCleanup>("TmpCleanup",
                x => x.Run(), Cron.Daily(3));
        jobs
            .AddOrUpdate<BackupCleanup>("BackupCleanup",
                x => x.Run(), Cron.Daily(4));
        jobs
            .AddOrUpdate<BiometricTransactionCleanup>("BiometricTransactionCleanup",
                x => x.Run(), Cron.Daily(5)); 

    }

    private static void ConfigureCalculationJobs(IRecurringJobManager jobs)
    {
        
         jobs
            .AddOrUpdate<ContributingImpactCalculation>("ContributingImpactCalculation",
                x => x.Run(), @"*/10 * * * *"); 
         
         jobs
             .AddOrUpdate<RiskScoreCalculation>("RiskScoreCalculation",
                 x => x.Run(), @"0 */2 * * *"); 
         
         //DEBUG
         //jobs.AddOrUpdate<BackupWork>("DebugService", x => x.Run(), @"*/1 * * * *"); 
            
    }
}