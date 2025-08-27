using DAL.Entities;

namespace ServerServices.Events;

public class RiskCalculationEventArgs: EventArgs
{
    public RiskScoring RiskScoring { get; set; } = new RiskScoring();

}