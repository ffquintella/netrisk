using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
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

    public float GetVulnerabilitiesVerifiedPercentage()
    {
        float result = 0;

        using var dbContext = DalService.GetContext();
        var vulnerabilities = dbContext.Vulnerabilities.Where(v => v.Score > 2 ).AsNoTracking();

        var total = vulnerabilities.Count();
        
        var verified = vulnerabilities.Count(v => v.Status != (int)IntStatus.New);
        
        result = (float)verified / total * 100;
        
        return result;
    }

    public VulnerabilityNumbers GetVulnerabilityNumbers()
    {
        var result = new VulnerabilityNumbers();
        using var dbContext = DalService.GetContext();
        var vulnerabilities = dbContext.Vulnerabilities.AsNoTracking();
        
        result.Critical = vulnerabilities.Count(v => v.Severity == "4");
        result.High = vulnerabilities.Count(v => v.Severity == "3");
        result.Medium = vulnerabilities.Count(v => v.Severity == "2");
        result.Low = vulnerabilities.Count(v => v.Severity == "1");
        result.Insignificant = vulnerabilities.Count(v => v.Severity == "0");
        result.Total = vulnerabilities.Count();
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