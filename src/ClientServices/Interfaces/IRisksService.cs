using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IRisksService
{
    /// Returns the list of risks
    public List<Risk> GetAllRisks(bool includeClosed = false);
    
    /// <summary>
    /// Gets the list of risks for the current user
    /// </summary>
    /// <returns></returns>
    public List<Risk> GetUserRisks();
    
    /// <summary>
    /// Gets the list of risks for the current user
    /// </summary>
    /// <returns></returns>
    public Task<List<Risk>> GetUserRisksAsync();
    
    public string GetRiskCategory(int id);
    public Task<string> GetRiskCategoryAsync(int id);
    public Task<RiskScoring> GetRiskScoringAsync(int id);
    public RiskScoring GetRiskScoring(int id);
    public List<Category>? GetRiskCategories();
    public string GetRiskSource(int id);
    public Task<string> GetRiskSourceAsync(int id);
    public Closure? GetRiskClosure(int riskId);

    public List<CloseReason> GetRiskCloseReasons();
    
    public List<Source>? GetRiskSources();
    
    public List<Likelihood>? GetProbabilities();
    
    public Task<List<Likelihood>?> GetProbabilitiesAsync();
    
    public List<Impact>? GetImpacts();
    
    public Task<List<Impact>?> GetImpactsAsync();
    
    public float GetRiskScore(int probabilityId, int impactId);
    
    public List<RiskCatalog> GetRiskTypes(string ids,  bool all = false);
    
    public List<RiskCatalog> GetRiskTypes();
    
    public Task<List<RiskCatalog>> GetRiskTypesAsync(string ids,  bool all = false);
    public Task<List<RiskCatalog>> GetRiskTypesAsync();
    
    public bool RiskSubjectExists(string status);

    public Risk? CreateRisk(Risk risk);
    
    public void SaveRisk(Risk risk);
    
    public void DeleteRisk(Risk risk);
    
    public RiskScoring? CreateRiskScoring(RiskScoring scoring);
    
    public void SaveRiskScoring(RiskScoring scoring);
    
    public void DeleteRiskScoring(int scoringId);
    /// <summary>
    /// Closes the risk with the specified closure
    /// </summary>
    /// <param name="closure"></param>
    public void CloseRisk(Closure closure);
    
    /// <summary>
    /// Returns a list of files associated to a specific risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public List<FileListing> GetRiskFiles(int riskId);
    
    /// <summary>
    /// Returns a list of files associated to a specific risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Task<List<FileListing>> GetRiskFilesAsync(int riskId);
    
    /// <summary>
    /// Adds an entity to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <param name="entityId"></param>
    public void AssociateEntityToRisk(int riskId, int entityId);
    
    /// <summary>
    /// Gets the id of the entity associated to the risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Int32? GetEntityIdFromRisk(int riskId);
    
    /// <summary>
    /// Gets the list of management reviews associated to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public List<MgmtReview> GetRiskMgmtReviews(int riskId);
    
    /// <summary>
    ///  Gets the review level associated to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public ReviewLevel GetRiskReviewLevel(int riskId);

    /// <summary>
    /// Gets the last management review associated to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public MgmtReview? GetRiskLastMgmtReview(int riskId);
    
    /// <summary>
    /// Gets the list of risk reviews associated to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Task<MgmtReview?> GetRiskLastMgmtReviewAsync(int riskId);
    
    /// <summary>
    /// Gets the list of risk reviews associated to a risk
    /// </summary>
    /// <param name="daysSinceLastReview"></param>
    /// <param name="status"></param>
    /// <param name="includeNew"></param>
    /// <returns></returns>
    public List<Risk> GetToReview(  int daysSinceLastReview, string? status = null,  bool includeNew = false);

    /// <summary>
    /// Gets the list of risk vulnerabilities associated to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Task<List<Vulnerability>> GetVulnerabilitiesAsync(int riskId);

}