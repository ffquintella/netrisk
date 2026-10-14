using DAL.Entities;
using ServerServices.Services;
using Serilog;
using ServerServices.Interfaces;

namespace BackgroundJobs.Jobs.Calculation;

public class RiskScoreCalculation: BaseJob, IJob
{
    private IRiskCalculationService _riskCalculationService;
    
    public RiskScoreCalculation(ILogger logger, DalService dalService, IRiskCalculationService calculationService) : base(logger, dalService)
    {
        _riskCalculationService = calculationService;
    }

    public void Run()
    {
        // Blocking, as every other job here does. The call used to be discarded un-awaited, so Hangfire
        // marked the job succeeded before the pass had read a row and any exception it threw was lost
        // with the task — a failing matrix pass looked like a green one (T236, S39).
        _riskCalculationService.CalculateRiskScoreAsync().GetAwaiter().GetResult();
        
        /*
        using var context = DalService.GetContext();
        
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

        }
        
        Log.Information("Risk scores calculated");
        */
        
    }
}