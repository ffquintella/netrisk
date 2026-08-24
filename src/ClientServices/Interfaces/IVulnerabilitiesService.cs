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
    public Task UpdateAsync(Vulnerability vulnerability);
    
    /// <summary>
    /// Associate risks to a vulnerability
    /// </summary>
    /// <param name="vulnerabilityId"></param>
    /// <param name="riskIds"></param>
    public Task AssociateRisksAsync(int vulnerabilityId, List<int> riskIds);
    
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

    // --- Track 3 (ASPM) ---------------------------------------------------------------------

    /// <summary>Everything the server can import with, for the import dialog's picker (3.1.5).</summary>
    Task<List<Model.Findings.ImporterDescriptor>> GetImportersAsync();

    /// <summary>
    /// Starts an import of an already-uploaded file. The importer name <c>auto</c> asks the server
    /// to detect the format from the file's content.
    /// </summary>
    Task<Model.Findings.ImportJobStatus> StartImportAsync(string importerName, string fileId,
        bool ignoreNegligible = true);

    /// <summary>The status and counts of an import, for progress and the final summary.</summary>
    Task<ScanImport> GetImportAsync(int importId);

    Task<List<ScanImport>> GetImportsAsync(int take = 50);

    /// <summary>Moves a finding through the triage lifecycle (3.2.1).</summary>
    Task<Vulnerability> UpdateLifecycleStatusAsync(int findingId, DAL.Enums.FindingStatus status,
        string? justification = null, int? duplicateOfId = null);

    /// <summary>The finding's audit timeline (3.2.2), newest first.</summary>
    Task<List<FindingStatusHistory>> GetStatusHistoryAsync(int findingId);

    /// <summary>Which states the finding may move to, so the UI offers only legal actions.</summary>
    Task<List<DAL.Enums.FindingStatus>> GetAllowedTransitionsAsync(int findingId);

    /// <summary>SLA compliance by severity, for the dashboard widget (3.4.2).</summary>
    Task<List<Model.Findings.SlaComplianceView>> GetSlaComplianceAsync();
    
    /// <summary>
    /// Get the last scan date
    /// </summary>
    /// <returns></returns>
    public Task<DateTime> GetLastScanDateAsync();
}