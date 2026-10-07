using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// Nightly reconciliation of the derived mandatory flags (Stage 9.5, S46 §4.6), at 05:40.
///
/// After the CISA KEV sync (05:00) and the EPSS sync (05:20), so a CVE listed or delisted overnight moves
/// flag 3 the same night; before the risk-acceptance expiry passes (06:00, 06:15) and the review cadence
/// (07:30), so a Gate A onset is escalated — and marks the risk for review — before those read the register.
///
/// A thin wrapper, like the sync jobs: the service writes only what changed, so a re-run is harmless, and
/// this job never propagates an exception — Hangfire would retry it immediately against the same failure.
/// </summary>
public class RiskFlagsDerivationJob(
    ILogger logger,
    DalService dalService,
    IRiskFlagsService flags)
    : BaseJob(logger, dalService), IJob
{
    public const string JobId = "RiskFlagsDerivation";

    /// <summary>After KEV (05:00) and EPSS (05:20), before the expiry passes (06:00, 06:15).</summary>
    public const string Cron = "40 5 * * *";

    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            var summary = await flags.RefreshAllAsync();

            Log.Information(
                "Risk flags derivation: {Risks} risk(s), {Raised} raised, {Reverted} reverted, {Onsets} Gate A onset(s)",
                summary.RisksEvaluated, summary.FlagsRaised, summary.FlagsReverted, summary.GateAOnsets);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The risk flags derivation pass failed");
        }
    }
}
