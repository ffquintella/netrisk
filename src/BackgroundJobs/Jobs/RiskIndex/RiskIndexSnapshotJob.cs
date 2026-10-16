using System;
using System.Threading.Tasks;
using Hangfire;
using Serilog;
using ServerServices.RiskIndex;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.RiskIndex;

/// <summary>
/// The daily official snapshot of the Consolidated Risk Index (M53, T247; S39 §9.1, S40 §9.3), at 05:00 UTC: after the
/// residual pass (02:20) and the Vision One (03:00) and SecurityScorecard (04:00) syncs, before the 07:00 digests. The
/// order is pinned by <c>RiskIndexJobScheduleTest</c>. A thin wrapper: the service writes a day once per profile.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 3600)]
public class RiskIndexSnapshotJob(ILogger logger, DalService dalService, IRiskIndexSnapshotService snapshots)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "RiskIndexSnapshot";

    public const string Cron = "0 5 * * *";

    public void Run() => RunAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Never propagates: Hangfire would retry at once against the same failure. A failed day is logged (here and in
    /// <c>integration_sync_logs</c>) and stays a gap in the trend, as S40 §9.3 prescribes.
    /// </summary>
    public async Task<RiskIndexSnapshotOutcome?> RunAsync()
    {
        try
        {
            var outcome = await snapshots.RunAsync(DateTime.UtcNow);
            if (outcome.Result == RiskIndexSnapshotResult.Failed)
                Log.Error("Risk index snapshot of {Date} failed: {Error}", outcome.Date, outcome.Error);
            return outcome;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The risk index snapshot job failed");
            return null;
        }
    }
}
