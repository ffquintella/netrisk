using DAL;
using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class VulnerabilitiesService: ServiceBase, IVulnerabilitiesService
{
    public VulnerabilitiesService(ILogger logger, DALManager dalManager) : base(logger, dalManager)
    {
    }
    
    public List<Vulnerability> GetAll()
    {
        using var dbContext = DalManager.GetContext();
        
        List<Vulnerability> vulnerabilities = dbContext.Vulnerabilities.ToList();
        
        return vulnerabilities;
    }
}