using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Statistics;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class StatisticsService: ServiceBase, IStatisticsService
{
    public StatisticsService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public List<ValueName> GetVulnerabilitiesDistribution()
    {
        var result = new List<ValueName>();
        
        using var dbContext = DalService.GetContext();

        var vulnerabilities = dbContext.Vulnerabilities.AsNoTracking();
        var impacts = dbContext.Impacts.AsNoTracking();
        
        var severities = vulnerabilities.Select(v => v.Severity).Distinct().ToList();

        foreach (var severity in severities)
        {
            if(string.IsNullOrEmpty(severity)) continue;
            var intSeverity = Int32.Parse(severity);
            var impact = impacts.FirstOrDefault(i => i.Value == intSeverity + 1);

            var value = new ValueName()
            {
                Name = impact?.Name ?? "Unknown",
                Value = vulnerabilities.Count(v => v.Severity == severity)
            };
            result.Add(value);

        }
        
        
        return result;
    }
    
    public List<LabeledPoints> GetRisksVsCosts(double minRisk, double maxRisk)
    {
        using var dbContext = DalService.GetContext();

        var risks = dbContext.Risks.Include(r => r.Mitigation)
            .ThenInclude(mitigation => mitigation!.MitigationCostNavigation)
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

            if(riskScore.CalculatedRisk > maxRisk || riskScore.CalculatedRisk < minRisk) continue;
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