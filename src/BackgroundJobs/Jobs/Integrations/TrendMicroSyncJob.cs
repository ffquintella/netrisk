using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Integrations;

/// <summary>
/// Daily Trend Micro Vision One synchronization (Track 4 milestone 4.4).
///
/// The job asks the service which connections are due rather than syncing all of them: a tenant with a
/// six-hour interval and one with a weekly interval are both served by one recurring job, and a
/// connection synced by hand from the admin UI resets its own clock.
/// </summary>
public class TrendMicroSyncJob(
    ILogger logger,
    DalService dalService,
    ITrendMicroService trendMicro)
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
            var result = await trendMicro.SyncDueConnectionsAsync(DateTime.UtcNow);

            if (result.HostsCreated + result.HostsUpdated + result.FindingsCreated
                + result.FindingsUpdated + result.Errors == 0)
                return;

            Log.Information(
                "Vision One sync: {HostsCreated} host(s) created, {HostsUpdated} updated, "
                + "{FindingsCreated} finding(s) created, {FindingsUpdated} updated, "
                + "{Patched} closed by virtual patch, {Errors} error(s)",
                result.HostsCreated, result.HostsUpdated, result.FindingsCreated, result.FindingsUpdated,
                result.VirtualPatchesApplied, result.Errors);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The Trend Micro Vision One synchronization pass failed");
        }
    }
}
