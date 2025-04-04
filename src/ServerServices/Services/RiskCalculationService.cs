using DAL.Entities;
using Serilog;
using ServerServices.Events;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class RiskCalculationService(    
    ILogger logger,
    IDalService dalService ): ServiceBase(logger, dalService), IRiskCalculationService
{
    
    public event EventHandler<RiskCalculationEventArgs> RiskScoreCalculated = delegate { };
    protected virtual void OnRiskScoreCalculated(RiskCalculationEventArgs e)
    {
        RiskScoreCalculated.Invoke(this, e);
    }
    
    public async Task CalculateRiskScoreAsync()
    {
        await using var context = DalService.GetContext();
        
        //Console.WriteLine("Calculating risk scores");
        
        // Get Risks with vulnerabilities
        var risks = context.Risks
            .ToList();
        
        var riskModelValues = context.CustomRiskModelValues.ToList();


        foreach (var risk in risks)
        {
            var scoring = context.RiskScorings.FirstOrDefault(rs => rs.Id == risk.Id);

            if (scoring == null)
                scoring = new RiskScoring()
                {
                    Id = risk.Id,
                    ScoringMethod = 1,
                    ClassicImpact = 2,
                    ClassicLikelihood = 2,
                };
            

            scoring.CalculatedRisk =  Convert.ToSingle(riskModelValues.FirstOrDefault(rmv => rmv.Impact == scoring.ClassicImpact && rmv.Likelihood == scoring.ClassicLikelihood)?.Value ?? 0.0);
            context.SaveChanges();
            
            OnRiskScoreCalculated( new RiskCalculationEventArgs()
            {
                RiskScoring = scoring,
            });

        }
        
        Log.Information("Risk scores calculated");
    }

    public Task CalculateContributingImpactAsync()
    {
        throw new NotImplementedException();
    }
}