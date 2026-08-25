using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Integrations;

/// <summary>
/// Daily SecurityScorecard synchronization (Track 4 milestone 4.5).
///
/// Same shape as the Vision One job, and for the same reason: per-connection intervals decided by the
/// service, one recurring job.
/// </summary>
public class SecurityScorecardSyncJob(
    ILogger logger,
    DalService dalService,
    ISecurityScorecardService securityScorecard)
    : BaseJob(logger, dalService), IJob
{
    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            var result = await securityScorecard.SyncDueConnectionsAsync(DateTime.UtcNow);

            if (result.PostureRowsWritten + result.FindingsCreated + result.FindingsUpdated
                + result.Errors == 0)
                return;

            Log.Information(
                "SecurityScorecard sync: {Posture} posture row(s), {FindingsCreated} finding(s) created, "
                + "{FindingsUpdated} updated, index {Index}, {Errors} error(s)",
                result.PostureRowsWritten, result.FindingsCreated, result.FindingsUpdated,
                result.CyberRiskIndex, result.Errors);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The SecurityScorecard synchronization pass failed");
        }
    }
}
