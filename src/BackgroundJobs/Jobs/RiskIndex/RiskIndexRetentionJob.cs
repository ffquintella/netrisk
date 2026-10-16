using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.RiskIndex;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.RiskIndex;

/// <summary>
/// The only purge path of the index's history (M53, T247; S39 §9.1, S40 §9.6), at 02:40 UTC: after the governance
/// retention (02:30), in the quiet hour, long before the 05:00 snapshot reads the previous day.
/// </summary>
public class RiskIndexRetentionJob(ILogger logger, DalService dalService, IRiskIndexRetentionService retention)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "RiskIndexRetention";

    public const string Cron = "40 2 * * *";

    public void Run() => RunAsync().GetAwaiter().GetResult();

    public async Task<RiskIndexRetentionCounts?> RunAsync()
    {
        try
        {
            return await retention.ApplyAsync(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The risk index retention pass failed");
            return null;
        }
    }
}
