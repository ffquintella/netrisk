using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Statistics;
using Serilog;
using ServerServices.Interfaces;
using Tools.Risks;

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

    public List<ValueName> GetVulnerabilitySources()
    {
        var result = new List<ValueName>();
        
        using var dbContext = DalService.GetContext();

        var vulnerabilities = dbContext.Vulnerabilities.AsNoTracking();
        var sources = vulnerabilities.Select(v => v.ImportSource).Distinct().ToList();
        
        foreach (var source in sources)
        {
            if(string.IsNullOrEmpty(source)) continue;
            
            var sourceCount = vulnerabilities.Count(v => v.ImportSource == source);

            var value = new ValueName()
            {
                Name = source,
                Value = sourceCount
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

        if (total == 0) return 0;
        if (verified == 0) return 0;
        
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

    public VulnerabilityNumbersByStatus GetVulnerabilitiesNumbersByStatus()
    {
        var result = new VulnerabilityNumbersByStatus();
        
        using var dbContext = DalService.GetContext();

        var vulnerabilities = dbContext.Vulnerabilities.AsNoTracking();
        
        var statuses = vulnerabilities.Select(v => v.Status).Distinct().ToList();

        foreach (var status in statuses)
        {
            var svn = new VulnerabilityNumbers();
            svn.Critical = vulnerabilities.Count(v => v.Status == status && v.Severity == "4");
            svn.High = vulnerabilities.Count(v => v.Status == status && v.Severity == "3");
            svn.Medium = vulnerabilities.Count(v => v.Status == status && v.Severity == "2");
            svn.Low = vulnerabilities.Count(v => v.Status == status && v.Severity == "1");
            svn.Insignificant = vulnerabilities.Count(v => v.Status == status && v.Severity == "0");
            svn.Total = vulnerabilities.Count(v => v.Status == status);
            
            result.NumbersByStatus.Add(status, svn);
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
                //X = riskScore.CalculatedRisk,
                X = RiskCalculationTool.CalculateTotalRiskScore(riskScore.CalculatedRisk, (float?) riskScore.ContributingScore),
                Y = cost,
                Label = "R-"+risk.Id.ToString()
            });
        }

        return result;

    }

    public List<LabeledPoints> GetRisksImpactVsProbability(double minRisk, double maxRisk)
    {
        using var dbContext = DalService.GetContext();

        var risks = dbContext.Risks.ToList();

        var riskScores = dbContext.RiskScorings.ToList();
        
        var result = new List<LabeledPoints>();
        
        foreach (var risk in risks)
        {
            var riskScore = riskScores.FirstOrDefault(r => r.Id == risk.Id);
            if (riskScore == null) continue;


            if(riskScore.CalculatedRisk > maxRisk || riskScore.CalculatedRisk < minRisk) continue;
            result.Add(new LabeledPoints
            {
                //X = riskScore.CalculatedRisk,
                X = riskScore.ClassicLikelihood,
                Y = riskScore.ClassicImpact,
                Label = "R-"+risk.Id.ToString()
            });
        }

        return result;
    }

    public List<ValueNameType> GetEntitiesRiskValues()
    {
        using var dbContext = DalService.GetContext();
        var result = new List<ValueNameType>();

        var entities = dbContext.Entities
            .Include(e => e.EntitiesProperties)
            .Include(e => e.Risks)
            .ToList();
        
        var scores = dbContext.RiskScorings.ToList();

        foreach (var entity in entities)
        {
            var eres = new ValueNameType()
            {
                Name = entity.EntitiesProperties.FirstOrDefault(p => p.Type == "name")?.Value + "",
                Value = scores.Where(s => entity.Risks.Select(r=> r.Id).Contains(s.Id)).Select(s => s.CalculatedRisk).Sum(),
                Type = entity.DefinitionName
            };
            
            eres.Value += GetChildEntitiesRiskScore(entity.Id);
            
            result.Add(eres);
        }

        return result.OrderByDescending(r=> r.Value).ToList();
    }

    private float GetChildEntitiesRiskScore(int id)
    {
        float totalScore = 0;
        using var dbContext = DalService.GetContext();
        var result = new List<ValueNameType>();

        var entity = dbContext.Entities
            .Include(e => e.EntitiesProperties)
            .Include(e => e.Risks)
            .Include(e => e.InverseParentNavigation).ThenInclude(ip => ip.Risks)
            .FirstOrDefault(e => e.Id == id);

        if (entity == null) return 0;
        
        var scores = dbContext.RiskScorings.ToList();
        
        foreach (var echild in entity.InverseParentNavigation)
        {

            totalScore += GetChildEntitiesRiskScore(echild.Id);
        }
        
        totalScore += scores.Where(s => entity.Risks.Select(r => r.Id).Contains(s.Id)).Select(s => s.CalculatedRisk)
            .Sum();

        return totalScore;
    }
    

    public List<RisksOnDay> GetRisksOverTime(int daysSpan = 30)
    {
        var firstDay = DateTime.Now.Subtract(TimeSpan.FromDays(daysSpan));
        
        using var dbContext = DalService.GetContext();
        var risks = dbContext.Risks.Join(dbContext.RiskScorings, 
                risk => risk.Id,
                riskScoring => riskScoring.Id,
                (risk, riskScoring) => new
                {
                    Id = risk.Id,
                    SubmissionDate = risk.SubmissionDate,
                    CalculatedRisk = riskScoring.CalculatedRisk,
                    Status = risk.Status
                }).
            Where(risk => risk.SubmissionDate > firstDay).ToList();
        
        var result = new List<RisksOnDay>();

        var computingDay = firstDay;

        // First let's get the total prior to the first day
        var oldRisksCount = dbContext.Risks.Count(rsk => rsk.SubmissionDate.Date < computingDay.Date && rsk.Status != "Closed");
        
        while (computingDay < DateTime.Now)
        {
            var risksSelected = risks.Where(rsk => rsk.SubmissionDate.Date == computingDay.Date && rsk.Status != "Closed").ToList();

            oldRisksCount += risksSelected.Count;
            
            var riskOnDay = new RisksOnDay
            {
                Day = computingDay,
                RisksCreated = 0,
                TotalRiskValue = 0,
                TotalRisks = oldRisksCount 
            };
            
            foreach (var risk in risksSelected)
            {
                riskOnDay.RisksCreated++;
                riskOnDay.TotalRiskValue += risk.CalculatedRisk;
            }
            result.Add(riskOnDay);
            computingDay = computingDay.AddDays(1);
        }

        return result;
    }
    
}