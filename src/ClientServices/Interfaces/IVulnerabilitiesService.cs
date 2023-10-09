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
    
    
    /// <summary>
    /// Get all risks scores for a vulnerability
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <returns></returns>
    public List<RiskScoring> GetRisksScores(int vulnerabilityId);
    
    /// <summary>
    /// Creates a new vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    /// <returns></returns>
    public Vulnerability Create(Vulnerability vulnerability);
    
    /// <summary>
    /// Updates a vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    public void Update(Vulnerability vulnerability);
    
    /// <summary>
    /// Associate risks to a vulnerability
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <param name="riskIds"></param>
    public void AssociateRisks(int vulnerabilityId, List<int> riskIds);
    
    /// <summary>
    /// Delete a vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    public void Delete(Vulnerability vulnerability);
    
    /// <summary>
    /// Update the status of a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="status"></param>
    public void UpdateStatus(int id, ushort status);
}