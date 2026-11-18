using System;
using System.Threading.Tasks;
using Hangfire;
using Model.Integrations;
using Serilog;
using ServerServices.Integrations.Tenable;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Integrations;

/// <summary>
/// Tenable Vulnerability Management (M56, T265; S40 §9.1, §9.4), every 15 minutes: claims the manual requests the API
/// queued, then runs the connections whose anchored schedule is due. Here, not in the API, so an hours-long export never
/// holds a web worker and the identity chain runs under the global background principal (T237). Single-flight.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public class TenableSyncJob(ILogger logger, DalService dalService, ITenableService tenable)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "TenableSync";

    public const string Cron = "*/15 * * * *";

    public void Run() => RunAsync().GetAwaiter().GetResult();

    /// <summary>Never propagates: each run settles its own sync-log row, and Hangfire must not retry a pass at once.</summary>
    public async Task<TenableSyncResult?> RunAsync()
    {
        try
        {
            var result = await tenable.RunPendingAsync(DateTime.UtcNow);
            if (result.Runs > 0)
                Log.Information(
                    "Tenable sync: {Runs} run(s), {Assets} asset(s), {HostsCreated} host(s) created, {HostsUpdated} updated, "
                    + "{FindingsCreated} finding(s) created, {FindingsUpdated} updated, {Fixed} fixed, {Errors} error(s)",
                    result.Runs, result.AssetsReceived, result.HostsCreated, result.HostsUpdated, result.FindingsCreated,
                    result.FindingsUpdated, result.FindingsFixed, result.Errors);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("The Tenable synchronization pass failed ({Type})", ex.GetType().Name);
            return null;
        }
    }
}
