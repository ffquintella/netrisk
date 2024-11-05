using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Statistics;
using Serilog;
using ServerServices.Interfaces;
using Tools.Risks;
using TopRisk = Model.Statistics.TopRisk;

namespace ServerServices.Services;

public class StatisticsService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IStatisticsService
{
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

    public async Task<List<ImportSeverity>> GetVulnerabilitiesServerityByImportAsync(int itemCount = 90)
    {

        await using var dbContext = DalService.GetContext();
        
        var imports = from v in dbContext.Vulnerabilities
            orderby v.FirstDetection.Date descending
            group v by new { v.FirstDetection.Date, v.Severity }into g
            select new ImportSeverity
            {
                ImportDate = g.Key.Date,
                ItemCount = g.Count(),
                TotalRiskValue = g.Sum(v => v.Score) ?? 0,
                CriticalityLevel = Convert.ToDouble(g.Key.Severity)
            };

        return await imports.Take(itemCount).ToListAsync();
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

    public async Task<RisksNumbers> GetRisksNumbersAsync()
    {

        var result = new RisksNumbers();
        
        await using var dbContext = DalService.GetContext();
        
        var risksScored = dbContext.Risks.Join(dbContext.RiskScorings, 
                risk => risk.Id,
                riskScoring => riskScoring.Id,
                (risk, riskScoring) => new
                {
                    Id = risk.Id,
                    CalculatedRisk = riskScoring.CalculatedRisk, 
                    Status = risk.Status
                }).ToList();
        
        result.General.Total = risksScored.Count;
        result.General.High = risksScored.Count(r => r.CalculatedRisk > 7);
        result.General.Medium = risksScored.Count(r => r.CalculatedRisk is > 4 and <= 7);
        result.General.Low = risksScored.Count(r => r.CalculatedRisk <= 4);
        
        var statuses = risksScored.Select(r => r.Status).Distinct().ToList();
        
        foreach (var status in statuses)
        {
            result.ByStatus.Statuses.Add(status, risksScored.Count(r => r.Status == status));
        }
        

        return result;

    }

    public async Task<List<RiskGroup>> GetRisksTopGroupsAsync()
    {
        var risks = await GetRisksTopAsync(100);

        var groups = risks.GroupBy(r => r.Group)
            .Select(r => new
            {
                Group = r.Key,
                Score = r.Sum(s => s.Score),
                Count = r.Count()
            }).OrderByDescending(g => g.Score);


        var result = new List<RiskGroup>();
        
        foreach (var group in groups)
        {
            result.Add(new RiskGroup()
            {
                Name = group.Group,
                Score = group.Score,
                ItemCount = group.Count
            });
        }

        return result;
    }

    public async Task<List<RiskEntity>> GetRisksTopEntities(int count = 10)
    {
        await using var dbContext = DalService.GetContext();
        
        //var result = new List<RiskEntity>();

        var topEntities = dbContext.Risks.Include(r => r.Entities)
            .Join(dbContext.RiskScorings,
                risk => risk.Id,
                riskScoring => riskScoring.Id,
                (risk, riskScoring) => new
                {
                    Id = risk.Id,
                    CalculatedRisk = riskScoring.CalculatedRisk,
                    Status = risk.Status,
                    risk.Owner,
                    risk.Manager,
                    risk.SubmissionDate,
                    risk.LastUpdate,
                    risk.Subject,
                    Entities = risk.Entities
                })
            .SelectMany(e => e.Entities, (er, entity) => new
            {
                Entity = entity,
                CalculatedRisk = er.CalculatedRisk
            }).GroupBy( e => e.Entity.Id)
            .Select(g => new RiskEntity
            {
                EntityId = g.Key,
                EntityType = g.First().Entity.DefinitionName,
                EntityName = dbContext.EntitiesProperties.FirstOrDefault(ep => ep.Entity == g.Key && ep.Type == "name")!.Value ?? "",
                TotalCalculatedRisk = g.Sum(re => re.CalculatedRisk)
            })
            .OrderByDescending(er => er.TotalCalculatedRisk)
            .Take(count)
            .ToList();


        return topEntities;

    }
    
    public async Task<List<TopRisk>> GetRisksTopAsync(int topCount = 10)
    {
        var result = new List<TopRisk>();

        await using var dbContext = DalService.GetContext();
        
        var risksScored = dbContext.Risks
            .Join(dbContext.RiskScorings, 
            risk => risk.Id,
            riskScoring => riskScoring.Id,
            (risk, riskScoring) => new
            {
                Id = risk.Id,
                CalculatedRisk = riskScoring.CalculatedRisk, 
                Status = risk.Status,
                Name = risk.Subject,
                Category = risk.Category
                
            })
            .Join(dbContext.RiskGroupings, 
                risk => risk.Category,
                grouping => grouping.Value
                , (risk, grouping) => new
                {
                    risk.Id,
                    risk.CalculatedRisk,
                    risk.Status,
                    risk.Name,
                    Group = grouping.Name
                })
            .OrderByDescending(cr => cr.CalculatedRisk).Take(topCount)
            .ToList();

        foreach (var risk in risksScored)
        {
            result.Add(new TopRisk()
            {
                Name = risk.Name,
                Score = risk.CalculatedRisk,
                Group = risk.Group
            });
        }

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

    public List<ValueNameType> GetEntitiesRiskValues(int? parentId = null, int topCount = 10)
    {
        using var dbContext = DalService.GetContext();
        var result = new List<ValueNameType>();

        List<Entity> entities;
        
        if(parentId == null)
            entities = dbContext.Entities
                .Include(e => e.EntitiesProperties)
                .Include(e => e.Risks)
                .ToList();
        else
            entities = dbContext.Entities
                .Include(e => e.EntitiesProperties)
                .Include(e => e.Risks)
                .Where(e => e.Parent == parentId).ToList();

        
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

        return result.OrderByDescending(r=> r.Value).Take(topCount).ToList();
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
                    SubmissionDate = risk.SubmissionDate.Date,
                    LastUpdate = risk.LastUpdate.Date,
                    CalculatedRisk = riskScoring.CalculatedRisk,
                    Status = risk.Status
                }).
            Where(risk => risk.SubmissionDate > firstDay || risk.LastUpdate > firstDay).ToList();
        
        var result = new List<RisksOnDay>();

        var computingDay = firstDay;

        // First let's get the total prior to the first day
        var oldRisksCount = dbContext.Risks.Count(rsk => rsk.SubmissionDate.Date < computingDay.Date && rsk.Status != "Closed");
        
        // Let's get the ones closed on the period
        oldRisksCount += dbContext.Risks.Count(rsk => rsk.LastUpdate.Date > computingDay.Date && rsk.Status == "Closed");
        
        while (computingDay < DateTime.Now)
        {
            var opened = risks.Where(rsk => rsk.SubmissionDate.Date == computingDay.Date && rsk.Status != "Closed").ToList();

            var closed = risks.Where(rsk => 
                rsk.LastUpdate.Date == computingDay.Date &&
                rsk.Status.ToLower() == "closed"
                ).ToList();
            
            oldRisksCount += opened.Count - closed.Count;
            
            var riskOnDay = new RisksOnDay
            {
                Day = computingDay,
                RisksCreated = 0,
                TotalRiskValue = 0,
                TotalRisks = oldRisksCount 
            };
            
            foreach (var risk in opened)
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