using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IVulnerabilitiesService
{
    /// <summary>
    /// Get all vulnerabilities
    /// </summary>
    /// <returns></returns>
    public List<Vulnerability> GetAll();
    
    /// <summary>
    /// Get one vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Vulnerability GetOne(int id);
}