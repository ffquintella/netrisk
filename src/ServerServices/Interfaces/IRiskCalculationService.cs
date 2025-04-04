using ServerServices.Events;

namespace ServerServices.Interfaces;

public interface IRiskCalculationService
{
    
    public event EventHandler<RiskCalculationEventArgs> RiskScoreCalculated;
    
    public Task CalculateRiskScoreAsync();
    public Task CalculateContributingImpactAsync();
}