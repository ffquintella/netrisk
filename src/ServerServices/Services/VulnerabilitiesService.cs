using AutoMapper;
using DAL;
using DAL.Entities;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class VulnerabilitiesService: ServiceBase, IVulnerabilitiesService
{
    private IMapper Mapper { get; }
    public VulnerabilitiesService(ILogger logger, DALManager dalManager, IMapper mapper) : base(logger, dalManager)
    {
        Mapper = mapper;
    }
    
    public List<Vulnerability> GetAll()
    {
        using var dbContext = DalManager.GetContext();
        
        List<Vulnerability> vulnerabilities = dbContext.Vulnerabilities.ToList();
        
        return vulnerabilities;
    }

    public Vulnerability GetById(int vulnerabilityId)
    {
        using var dbContext = DalManager.GetContext();

        var vulnerability = dbContext.Vulnerabilities.Find(vulnerabilityId);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerabilityId.ToString(), 
            new Exception("Vulnerability not found"));
        
        return vulnerability;
    }

    public void Delete(int vulnerabilityId)
    {
        using var dbContext = DalManager.GetContext();

        var vulnerability = dbContext.Vulnerabilities.Find(vulnerabilityId);
        
        if( vulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerabilityId.ToString(),
            new Exception("Vulnerability not found"));
        
        dbContext.Vulnerabilities.Remove(vulnerability);
        dbContext.SaveChanges();
    }

    public Vulnerability Create(Vulnerability vulnerability)
    {
        vulnerability.Id = 0;
        using var dbContext = DalManager.GetContext();

        var newVulnerability = dbContext.Vulnerabilities.Add(vulnerability);
        dbContext.SaveChanges();
        
        return newVulnerability.Entity;
    }

    public void Update(Vulnerability vulnerability)
    {
        if(vulnerability == null) throw new ArgumentNullException(nameof(vulnerability));
        if(vulnerability.Id == 0) throw new ArgumentException("Vulnerability id cannot be 0");
        
        using var dbContext = DalManager.GetContext();
        
        var dbVulnerability = dbContext.Vulnerabilities.Find(vulnerability.Id);
        
        if( dbVulnerability == null) throw new DataNotFoundException("vulnerabilities",vulnerability!.Id.ToString(),
            new Exception("Vulnerability not found"));

        Mapper.Map(vulnerability, dbVulnerability);
        
        dbContext.SaveChanges();
    }
    
}