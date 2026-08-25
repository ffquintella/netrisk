using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Integrations;

/// <summary>
/// Retries pending notification deliveries and closes digest windows
/// (Track 4 milestone 4.1.1/4.1.3).
///
/// Runs every minute. An immediate send is attempted inline when the event is raised — so "new Critical
/// risk → Slack within seconds" does not wait for this job — and this is what makes a transient failure
/// eventually succeed, and what makes a digest window a real window rather than a delay until the next
/// event happens to arrive.
/// </summary>
public class NotificationDispatchJob(
    ILogger logger,
    DalService dalService,
    INotificationDispatcher dispatcher,
    INotificationSubscriptionsService subscriptions)
    : BaseJob(logger, dalService), IJob
{
    /// <summary>
    /// Delivery rows older than this are purged. Ninety days covers a quarter's audit; keeping them
    /// forever turns the log into the largest table in the database.
    /// </summary>
    private const int RetentionDays = 90;

    /// <summary>
    /// Purge runs on the pass that starts a new UTC day. Cheaper than a second recurring job, and the
    /// delete is a single indexed range.
    /// </summary>
    internal static DateTime LastPurgeDate = DateTime.MinValue;

    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        try
        {
            var result = await dispatcher.ProcessPendingAsync(now);

            if (result.Delivered + result.Failed + result.Retried + result.DigestsSent > 0)
                Log.Information(
                    "Notification sweep delivered {Delivered} ({Fallback} by fallback), retried {Retried}, "
                    + "failed {Failed}, sent {Digests} digest(s)",
                    result.Delivered, result.DeliveredByFallback, result.Retried, result.Failed,
                    result.DigestsSent);
        }
        catch (Exception ex)
        {
            // Caught here rather than left to Hangfire's retry: a sweep that throws should not be
            // re-run immediately with the same broken state, and the next minute's pass will try again.
            Log.Error(ex, "The notification delivery sweep failed");
        }

        if (LastPurgeDate.Date >= now.Date) return;

        LastPurgeDate = now;

        try
        {
            var purged = await subscriptions.PurgeDeliveriesAsync(RetentionDays);

            if (purged > 0)
                Log.Information("Purged {Count} notification delivery row(s) older than {Days} days",
                    purged, RetentionDays);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not purge old notification deliveries: {Message}", ex.Message);
        }
    }
}
