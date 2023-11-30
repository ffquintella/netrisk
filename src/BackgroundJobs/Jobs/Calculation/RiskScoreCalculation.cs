using DAL.Entities;
using ServerServices.Services;
using Serilog;   

namespace BackgroundJobs.Jobs.Calculation;

public class RiskScoreCalculation: BaseJob, IJob
{
    public RiskScoreCalculation(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public void Run()
    {
        using var context = DalService.GetContext();
        
        Console.WriteLine("Calculating risk scores");
        
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

        }
        
        Log.Information("Risk scores calculated");
        
    }
}