using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// The quarterly review of the archive (Stage 9.9, S50 §4.2), at 07:15: every live archive whose review date has come
/// is announced, once per due date, through <c>risk.archive_review_due</c>.
///
/// Without it the review is a date on a record nobody opens — the failure the Track 8 cadence job was written against.
/// After the KRI evaluation (06:45), which may reopen an archive whose KRI breached overnight (a reopened archive has no
/// review due), and before the review cadence (07:30). The order is pinned by <c>RiskArchiveReviewJobTest</c>.
///
/// A thin wrapper: the service records the notice on the archive, so a re-run announces nothing twice, and this job never
/// propagates an exception — Hangfire would retry it immediately against the same failure.
/// </summary>
public class RiskArchiveReviewJob(
    ILogger logger,
    DalService dalService,
    IRiskArchiveService archives)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "RiskArchiveReview";

    /// <summary>After the KRI evaluation (06:45), before the review cadence (07:30).</summary>
    public const string Cron = "15 7 * * *";

    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            var summary = await archives.NotifyDueReviewsAsync();

            Log.Information("Archive review sweep: {Live} live archive(s), {Due} due for review, {Notified} announced",
                summary.Live, summary.Due, summary.Notified);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The archive review sweep failed");
        }
    }
}
