using System;
using System.Threading.Tasks;
using Hangfire;
using Serilog;
using ServerServices.RiskIndex.Profiles;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.RiskIndex;

/// <summary>
/// The sensitivity previews of draft profiles (M53, T250; S39 §13.2, S40 §9.6), every minute: computed here, in the job
/// host, so a 90-day replay never competes with API requests and survives a restart. Single-flight by
/// <c>DisableConcurrentExecution</c>, and the service claims each request atomically, so two runs never compute one twice.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public class RiskIndexPreviewJob(ILogger logger, DalService dalService, IRiskIndexPreviewService previews)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "RiskIndexPreview";

    public const string Cron = "* * * * *";

    /// <summary>At most this many queued requests per run; the rest wait for the next minute.</summary>
    public const int MaxPerRun = 5;

    public void Run() => RunAsync().GetAwaiter().GetResult();

    /// <summary>Never propagates: a failed preview is recorded on its request, and Hangfire must not retry it at once.</summary>
    public async Task<int> RunAsync()
    {
        var processed = 0;
        try
        {
            for (var i = 0; i < MaxPerRun; i++)
            {
                var result = await previews.ProcessNextAsync(DateTime.UtcNow);
                if (result == RiskIndexPreviewRunResult.Idle) break;
                processed++;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The risk index preview job failed");
        }

        return processed;
    }
}
