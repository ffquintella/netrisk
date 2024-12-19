using DAL.Entities;
using Sieve.Models;

namespace ServerServices.Interfaces;

public interface IVulnerabilitiesService
{
    /// <summary>
    /// Get all vulnerabilities
    /// </summary>
    /// <returns></returns>
    public List<Vulnerability> GetAll();
    
    /// <summary>
    /// Get all vulnerabilities with filters
    /// </summary>
    /// <param name="sieveModel"></param>
    /// <param name="totalCount"></param>
    /// <param name="includeFixRequests"></param>
    /// <returns></returns>
    public List<Vulnerability> GetFiltred(SieveModel sieveModel, out int totalCount, bool includeFixRequests = false);
    
    /// <summary>
    /// Get vulnerability by id
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <returns></returns>
    public Vulnerability GetById(int vulnerabilityId, bool includeDetails = false);
    
    /// <summary>
    /// Get vulnerability by id
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <param name="includeDetails"></param>
    /// <returns></returns>
    public Task<Vulnerability> GetByIdAsync(int vulnerabilityId, bool includeDetails = false);
    
    
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
    public Task<Vulnerability> CreateAsync(Vulnerability vulnerability);
    
    /// <summary>
    /// Finds a vulnerability by hash
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    public Vulnerability Find(string hash);
    public Task<Vulnerability?> FindAsync(string hash);
    
    /// <summary>
    /// Update a vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    public void Update(Vulnerability vulnerability);
    public Task UpdateAsync(Vulnerability vulnerability);

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
    
    
    /// <summary>
    /// Update comments of a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="comments"></param>
    public void UpdateCommentsAsync(int id, string comments);

    /// <summary>
    /// Add an action to a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="userId"></param>
    /// <param name="actions"></param>
    public NrAction AddAction(int id, int userId, NrAction actions);
    
    /// <summary>
    /// Add a new action to a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="userId"></param>
    /// <param name="actions"></param>
    /// <returns></returns>
    public Task<NrAction> AddActionAsync(int id, int userId, NrAction actions);
    
    /// <summary>
    /// Get all vulnerabilities by host id
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public Task<List<Vulnerability>> GetVulnerabilitiesByHostIdAsync(int hostId);
}