using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// The daily evaluation of every key risk indicator (Stage 9.8, S49 §4.7, §4.10), at 06:45.
///
/// It is what notices the transitions nobody writes: a KRI whose latest reading ages past its maximum becomes stale
/// without anyone recording anything, and a risk linked during a breach gets its trigger. A reading recorded through
/// the API is evaluated at once; this pass catches the rest.
///
/// After the reconciliation of the derived flags (05:40) and the two expiry passes (06:00, 06:15); before the review
/// cadence (07:30), so a risk whose KRI breached overnight is flagged for review in time for that morning's
/// "flagged for review" message to its owner and manager rather than tomorrow's (S49 D10). The order is pinned by
/// <c>KriEvaluationJobTest</c>.
///
/// A thin wrapper, like the flags job: the service creates only what is missing — one event per breach episode, one
/// trigger per event and risk — so a re-run is harmless, and this job never propagates an exception: Hangfire would
/// retry it immediately against the same failure.
/// </summary>
public class KriEvaluationJob(
    ILogger logger,
    DalService dalService,
    IMonitoringService monitoring)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "KriEvaluation";

    /// <summary>After the flags (05:40) and the expiry passes (06:00, 06:15), before the review cadence (07:30).</summary>
    public const string Cron = "45 6 * * *";

    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            var summary = await monitoring.EvaluateAllAsync();

            Log.Information(
                "KRI evaluation: {Kris} KRI(s), {Breached} breached, {Stale} stale, {Opened} breach episode(s) opened, " +
                "{Closed} closed, {Triggers} reassessment trigger(s) raised",
                summary.KrisEvaluated, summary.Breached, summary.Stale, summary.EpisodesOpened, summary.EpisodesClosed,
                summary.TriggersRaised);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The KRI evaluation pass failed");
        }
    }
}
