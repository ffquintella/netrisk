using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// Get all vulnerabilities with filters
    /// </summary>
    /// <returns></returns>
    public List<Vulnerability> GetFiltered(int pageSize, int pageNumber, string filter, out int totalRecords, out bool validFilter);
    
    /// <summary>
    /// Get all vulnerabilities with filters
    /// </summary>
    /// <param name="pageSize"></param>
    /// <param name="pageNumber"></param>
    /// <param name="filter"></param>
    /// <param name="totalRecords"></param>
    /// <param name="validFilter"></param>
    /// <param name="includeFixRequests"></param>
    /// <returns></returns>
    public Task<Tuple<List<Vulnerability>,int,bool>> GetFilteredAsync(int pageSize, int pageNumber, string filter, bool includeFixRequests = false);
    
    /// <summary>
    /// Get one vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Vulnerability GetOne(int id);
    
    /// <summary>
    /// Get one vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<Vulnerability> GetOneAsync(int id);
    
    
    /// <summary>
    /// Get all risks scores for a vulnerability
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <returns></returns>
    public List<RiskScoring> GetRisksScores(int vulnerabilityId);
    
    /// <summary>
    /// Get all risks scores for a vulnerability
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <returns></returns>
    public Task<List<RiskScoring>> GetRisksScoresAsync(int vulnerabilityId);
    
    /// <summary>
    /// Creates a new vulnerability
    /// </summary>
    /// <param name="vulnerability"></param>
    /// <returns></returns>
    public Task<Vulnerability> CreateAsync(Vulnerability vulnerability);

    /// <summary>
    /// Find a vulnerability by hash
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    public Task<Tuple<bool,Vulnerability?>> FindAsync(string hash);
   
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
    
    /// <summary>
    /// Update the status of a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="status"></param>
    /// <returns></returns>
    public Task UpdateStatusAsync(int id, ushort status);
    
    /// <summary>
    /// Update the comments of a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="comments"></param>
    public void UpdateCommentsAsync(int id, string comments);

    /// <summary>
    /// Add an action to a vulnerability
    /// </summary>
    /// <param name="id"></param>
    /// <param name="userId"></param>
    /// <param name="action"></param>
    public Task<NrAction> AddActionAsync(int id, int userId, NrAction action);
    
    /// <summary>
    /// Import Nessus Async
    /// </summary>
    /// <param name="id"></param>
    /// <param name="status"></param>
    public Task ImportNessusAsync(string id);
}