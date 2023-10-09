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

    /// <summary>
    /// Associate risks to a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="riskIds"></param>
    public void AssociateRisks(int id, List<int> riskIds);

    /// <summary>
    /// Update status of a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="status"></param>
    public void UpdateStatus(int id, ushort status);
}