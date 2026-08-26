using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Governance;

/// <summary>
/// Derives every risk's residual score from its treatment (Track 8 milestone 8.2.1).
///
/// A separate job from <c>RiskScoreCalculation</c> and scheduled after it: residual is a function of
/// the inherent score, so running them in the other order would compute today's residual from
/// yesterday's inherent.
/// </summary>
public class ResidualRiskCalculation(
    ILogger logger,
    DalService dalService,
    IRiskCalculationService calculationService,
    IQuantitativeRiskService quantitative)
    : BaseJob(logger, dalService), IJob
{
    public void Run() => RunAsync().GetAwaiter().GetResult();

    private async Task RunAsync()
    {
        // Quantitatively scored risks first: their simulation writes both the inherent and the
        // residual mapped score, and the qualitative pass below would otherwise overwrite the
        // residual with a matrix-derived number.
        var requantified = await quantitative.RecomputeAllAsync();

        var changed = await calculationService.CalculateResidualRiskAsync();

        Log.Information("Residual pass: {Requantified} quantitative risk(s) re-simulated, {Changed} " +
                        "residual score(s) updated", requantified, changed);
    }
}
