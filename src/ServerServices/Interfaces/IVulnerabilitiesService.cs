using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IVulnerabilitiesService
{
    /// <summary>
    /// Get all vulnerabilities
    /// </summary>
    /// <returns></returns>
    public List<Vulnerability> GetAll();
    
    /// <summary>
    /// Get vulnerability by id
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <returns></returns>
    public Vulnerability GetById(int vulnerabilityId, bool includeDetails = false);
    
    
    /// <summary>
    /// Delete vulnerability by id
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    public void Delete(int vulnerabilityId);
    
    
    /// <summary>
    /// Creates a new vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    /// <returns></returns>
    public Vulnerability Create(Vulnerability vulnerability);
    
    /// <summary>
    /// Update a vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    public void Update(Vulnerability vulnerability);
}