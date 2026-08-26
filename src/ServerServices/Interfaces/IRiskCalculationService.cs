using ServerServices.Events;

namespace ServerServices.Interfaces;

public interface IRiskCalculationService
{
    
    public event EventHandler<RiskCalculationEventArgs> RiskScoreCalculated;
    
    public event EventHandler<RiskCalculationEventArgs> RiskContributingImpactCalculated;
    
    public Task CalculateRiskScoreAsync();
    public Task CalculateContributingImpactAsync();

    /// <summary>
    /// Derives every risk's residual score from its treatment (Track 8 milestone 8.2.1) and
    /// historizes it beside the inherent score. Returns how many rows changed.
    ///
    /// A separate pass rather than part of <see cref="CalculateRiskScoreAsync"/>: residual depends on
    /// the inherent score, so it has to run after it, and a caller that only wants one of the two
    /// should not be made to run both.
    /// </summary>
    public Task<int> CalculateResidualRiskAsync();
}