using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Statistics;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class StatisticsService: ServiceBase, IStatisticsService
{
    public StatisticsService(ILogger logger, DALManager dalManager) : base(logger, dalManager)
    {
    }

    public List<LabeledPoints> GetRisksVsCosts()
    {
        using var dbContext = DALManager.GetContext();

        var risks = dbContext.Risks.Include(r => r.Mitigation)
            .ThenInclude(mitigation => mitigation.MitigationCostNavigation)
            .ThenInclude(r => r.Mitigations).ToList();

        var riskScores = dbContext.RiskScorings.ToList();
        
        var result = new List<LabeledPoints>();
        
        foreach (var risk in risks)
        {
            var riskScore = riskScores.FirstOrDefault(r => r.Id == risk.Id);
            if (riskScore == null) continue;
            var mitigation = risk.Mitigation;
            if (mitigation == null) continue;
            var cost = mitigation.MitigationCostNavigation.Value;

            result.Add(new LabeledPoints
            {
                X = riskScore.CalculatedRisk,
                Y = cost,
                Label = "R-"+risk.Id.ToString()
            });
        }

        return result;

    }
}