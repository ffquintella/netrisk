using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Calculation;

public class ContributingImpactCalculation: BaseJob, IJob
{
    public ContributingImpactCalculation(ILogger logger, DalService dalService) : base(logger, dalService)
    {
    }

    public void Run()
    {
        using var context = DalService.GetContext();
        
        Console.WriteLine("Calculating contributing impacts");

        try
        {
            // Get Risks with vulnerabilities
            var risks = context.Risks
                .Include(risk => risk.Vulnerabilities.Where(v => 
                    v.Status != (int)IntStatus.Closed ||
                    v.Status != (int)IntStatus.Solved ||
                    v.Status != (int)IntStatus.Rejected ||
                    v.Status != (int)IntStatus.Retired ||
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
                context.SaveChanges();
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