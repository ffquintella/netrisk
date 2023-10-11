using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class VulnerabilitiesService: ServiceBase, IVulnerabilitiesService
{
    private IMapper Mapper { get; }
    public VulnerabilitiesService(ILogger logger, DALService dalService, IMapper mapper) : base(logger, dalService)
    {
        Mapper = mapper;
    }
    
    public List<Vulnerability> GetAll()
    {
        using var dbContext = DalService.GetContext();
        
        List<Vulnerability> vulnerabilities = dbContext.Vulnerabilities.ToList();
        
        return vulnerabilities;
    }

    public Vulnerability GetById(int vulnerabilityId, bool includeDetails = false)
    {
        using var dbContext = DalService.GetContext();

        Vulnerability? vulnerability;
        
        if(!includeDetails) vulnerability = dbContext.Vulnerabilities.Find(vulnerabilityId);
        else
        {
            vulnerability = dbContext.Vulnerabilities
                .Include(vul => vul.FixTeam)
                .Include(vul => vul.Host)
                .Include(vul => vul.Risks).ThenInclude(risk => risk.CategoryNavigation)
                .Include(vul => vul.Risks).ThenInclude(r => r.SourceNavigation)
                .FirstOrDefault(vul => vulnerabilityId == vul.Id);
        }
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerabilityId.ToString(), 
            new Exception("Vulnerability not found"));
        
        return vulnerability;
    }

    public void Delete(int vulnerabilityId)
    {
        using var dbContext = DalService.GetContext();

        var vulnerability = dbContext.Vulnerabilities.Find(vulnerabilityId);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerabilityId.ToString(),
            new Exception("Vulnerability not found"));
        
        dbContext.Vulnerabilities.Remove(vulnerability);
        dbContext.SaveChanges();
    }

    public Vulnerability Create(Vulnerability vulnerability)
    {
        vulnerability.Id = 0;
        using var dbContext = DalService.GetContext();

        var newVulnerability = dbContext.Vulnerabilities.Add(vulnerability);
        dbContext.SaveChanges();
        
        return newVulnerability.Entity;
    }

    public void Update(Vulnerability vulnerability)
    {
        if(vulnerability == null) throw new ArgumentNullException(nameof(vulnerability));
        if(vulnerability.Id == 0) throw new ArgumentException("Vulnerability id cannot be 0");
        
        using var dbContext = DalService.GetContext();
        
        var dbVulnerability = dbContext.Vulnerabilities.Find(vulnerability.Id);
        
        if( dbVulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerability!.Id.ToString(),
            new Exception("Vulnerability not found"));

        Mapper.Map(vulnerability, dbVulnerability);
        
        dbContext.SaveChanges();
    }

    public void AssociateRisks(int id, List<int> riskIds)
    {
        using var dbContext = DalService.GetContext();
        
        var risks = dbContext.Risks.Where(r => riskIds.Contains(r.Id)).ToList();
        
        var vulnerability = dbContext.Vulnerabilities.Include(v=>v.Risks).FirstOrDefault(v => v.Id == id);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));


        foreach (var risk in risks)
        {
            if(vulnerability.Risks.Contains(risk)) continue;
            vulnerability.Risks.Add(risk);
        }
        

        dbContext.SaveChanges();
    }

    public void UpdateStatus(int id, ushort status)
    {
        using var dbContext = DalService.GetContext();
        var vulnerability = dbContext.Vulnerabilities.Find(id);
        if(vulnerability == null) throw new DataNotFoundException("vulnerabilities",id.ToString(),
            new Exception("Vulnerability not found"));
        vulnerability.Status = status;
        dbContext.SaveChanges();
    }
    
}