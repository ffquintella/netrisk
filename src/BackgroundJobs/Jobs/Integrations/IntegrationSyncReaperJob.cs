using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Integrations;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Integrations;

/// <summary>
/// Hourly sweep for integration sync runs left Running by a process that stopped (Track 4).
///
/// Hourly rather than daily because the row it settles is what blocks the next sync of that
/// connection: at a daily cadence a killed 03:00 Vision One run would refuse every manual retry until
/// the following morning. Hourly puts the worst case at one hour past the two-hour staleness horizon.
/// </summary>
public class IntegrationSyncReaperJob(
    ILogger logger,
    DalService dalService,
    IIntegrationSyncReaper reaper)
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
            // ReapAsync logs what it settled and swallows its own failures; nothing to report here
            // when it finds nothing, which is the normal case every hour of every day.
            await reaper.ReapAsync(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The abandoned integration-run sweep failed");
        }
    }
}
