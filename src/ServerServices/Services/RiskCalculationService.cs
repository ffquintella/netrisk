using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
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
    
    public event EventHandler<RiskCalculationEventArgs> RiskContributingImpactCalculated = delegate { };
    
    protected virtual void OnRiskContributingImpactCalculated(RiskCalculationEventArgs e)
    {
        RiskContributingImpactCalculated.Invoke(this, e);
    }
    
    public async Task CalculateRiskScoreAsync()
    {
        await using var context = DalService.GetContext();
        
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
            await context.SaveChangesAsync();
            
            OnRiskScoreCalculated( new RiskCalculationEventArgs()
            {
                RiskScoring = scoring,
            });

        }
        
        Log.Information("Risk scores calculated");
    }

    public async Task CalculateContributingImpactAsync()
    {
        await using var context = DalService.GetContext();
        
        Console.WriteLine("Calculating contributing impacts");

        try
        {
            // Get Risks with vulnerabilities
            var risks = context.Risks
                .Include(risk => risk.Vulnerabilities.Where(v => 
                    v.Status != (int)IntStatus.Closed &&
                    v.Status != (int)IntStatus.Solved &&
                    v.Status != (int)IntStatus.Rejected &&
                    v.Status != (int)IntStatus.Retired &&
                    v.Status != (int)IntStatus.Fixed
                ))
                .Where(r => r.Vulnerabilities.Count > 0)
                .ToList();

            const int NMaxVulDiv = 10;

            // Get all vulnerabilities for each risk
            foreach (var risk in risks)
            {
                var scoring = context.RiskScorings.FirstOrDefault(rs => rs.Id == risk.Id);
                if (scoring == null) continue;
                
                if(risk.Vulnerabilities.Count == 0) 
                {
                    scoring.ContributingScore = 0;
                    await context.SaveChangesAsync();
                    continue;
                }
                
                var topScore = risk.Vulnerabilities.Max(v => v.Score)!.Value;

                var deltaConst = 10 - topScore;

                var totalSum = risk.Vulnerabilities.Sum(v => v.Score)!.Value;

                var contributingRiskScore = 0 + topScore;
                foreach (var vul in risk.Vulnerabilities)
                {
                    var vulcontrib = (deltaConst / (topScore * NMaxVulDiv)) * vul.Score!.Value;
                    contributingRiskScore += vulcontrib;
                }

                if (contributingRiskScore > 10) contributingRiskScore = 10;
                scoring.ContributingScore = contributingRiskScore;
                await context.SaveChangesAsync();
                
                OnRiskScoreCalculated( new RiskCalculationEventArgs()
                {
                    RiskScoring = scoring,
                });
            }
            Log.Information("Contributing impacts calculated");
        }
        catch (Exception e)
        {
            Log.Error("Error calculating contributing impacts: {Message}", e.Message);
            Console.WriteLine(e);
            throw;
        }
    }
}