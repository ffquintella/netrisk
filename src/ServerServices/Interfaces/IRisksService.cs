using System.Collections.Generic;
using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IRisksService
{
    /// <summary>
    /// Lists all the risks the user has access to
    /// </summary>
    /// <param name="user"></param>
    /// <param name="status"> The risk status to use as filter</param>
    /// <param name="notStatus"> The risk status to use as not filter</param>
    /// <returns>List of risks</returns>
    /// <throws>UserNotAuthorizedException</throws>
    List<Risk> GetUserRisks(User user, string? status, string? notStatus = "Closed");

    
    /// <summary>
    /// Get risks that needs to be reviewed
    /// </summary>
    /// <param name="daysSinceLastReview"></param>
    /// <param name="status"></param>
    /// <param name="includeNew"></param>
    /// <returns></returns>
    List<Risk> GetToReview(  int daysSinceLastReview, string? status = null,  bool includeNew = false);
    
    /// <summary>
    /// Returns the risk with the given id
    /// </summary>
    /// <param name="id">Risk id</param>
    /// <returns>Risk object</returns>
    Risk GetRisk(int id);
    
    /// <summary>
    /// Gets the risk scoring
    /// </summary>
    /// <param name="id">Risk ID</param>
    /// <returns>Risk scoring object</returns>
    RiskScoring GetRiskScoring(int id);
    
    
    /// <summary>
    /// Gets the list of risk scoring
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    List<RiskScoring> GetRisksScoring(List<int> ids);
    
    /// <summary>
    ///  Gets the risk with id if the user has permission 
    /// </summary>
    /// <param name="user">User object</param>
    /// <param name="id">id</param>
    /// <returns>Risk Object</returns>
    Risk GetUserRisk(User user,int id);
    
    /// <summary>
    /// Gets all risks filtering optionaly by status
    /// </summary>
    /// <param name="status">the status to use as filter</param>
    /// <returns></returns>
    List<Risk> GetAll(string? status = null, string? notStatus = "Closed");
    
    
    /// <summary>
    /// Gets all risks filtering optionaly by status 
    /// </summary>
    /// <param name="status"></param>
    /// <param name="notStatus"></param>
    /// <param name="includeCatalogs"></param>
    /// <returns></returns>
    public Task<List<Risk>> GetAllAsync(string? status = null, string? notStatus = "Closed", bool includeCatalogs = true);

    /// <summary>
    /// Check if subject exists
    /// </summary>
    /// <param name="subject"></param>
    /// <returns>bool</returns>
    bool SubjectExists(string subject);

    /// <summary>
    /// Create a new risk
    /// </summary>
    /// <param name="risk">the risk object to create</param>
    /// <returns>a risk object with updated fields</returns>
    [Obsolete("Use CreateRiskAsync instead")]
    public Risk? CreateRisk(Risk risk);
    
    
    /// <summary>
    /// Creates a new risk asynchronously
    /// </summary>
    /// <param name="risk"></param>
    /// <returns></returns>
    public Task<Risk?> CreateRiskAsync(Risk risk);
    
    /// <summary>
    /// Creates a new risk scoring
    /// </summary>
    /// <param name="riskScoring"></param>
    /// <returns></returns>
    public RiskScoring? CreateRiskScoring(RiskScoring riskScoring);
    
    
    /// <summary>
    /// Saves a Risk Scoring
    /// </summary>
    /// <param name="riskScoring"></param>
    public void SaveRiskScoring(RiskScoring riskScoring);
    
    /// <summary>
    /// Deletes a risk scoring
    /// </summary>
    /// <param name="id"></param>
    public void DeleteRiskScoring(int id);
    
    /// <summary>
    /// Saves the risk to the database
    /// </summary>
    /// <param name="risk">the risk object to save</param>
    void SaveRisk(Risk risk);
    
    /// <summary>
    /// Deletes the risk from the database
    /// </summary>
    /// <param name="id">The id of the risk to delete</param>
    void DeleteRisk(int id);
    
    /// <summary>
    /// Gets the risk category
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    Category GetRiskCategory(int id);
    
    /// <summary>
    /// Gets the list of risk category
    /// </summary>
    /// <returns></returns>
    List<Category> GetRiskCategories();
    
    /// <summary>
    /// Gets the list of risk probability or likelihood
    /// </summary>
    /// <returns></returns>
    public List<Likelihood> GetRiskProbabilities();
    
    
    /// <summary>
    /// Gets the list of risk close reasons
    /// </summary>
    /// <returns></returns>
    public List<CloseReason> GetRiskCloseReasons();
    
    /// <summary>
    /// Returns risk closure by risk id
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Closure GetRiskClosureByRiskId(int riskId);
    
    /// <summary>
    /// Returns risk entity by risk id
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Entity GetRiskEntityByRiskId(int riskId);
    
    /// <summary>
    /// Associates a risk with an entity
    /// </summary>
    /// <param name="riskId"></param>
    /// <param name="entityId"></param>
    public void AssociateRiskWithEntity(int riskId, int entityId);
    
    
    /// <summary>
    /// Cleans all the risk entity associations
    /// </summary>
    /// <param name="riskId"></param>
    public void CleanRiskEntityAssociations(int riskId);
    
    /// <summary>
    /// Deletes a risk and entity association
    /// </summary>
    /// <param name="riskId"></param>
    /// <param name="entityId"></param>
    public void DeleteEntityAssociation(int riskId, int entityId);
    
    /// <summary>
    /// Creates a new risk closure
    /// </summary>
    /// <param name="closure"></param>
    /// <returns></returns>
    public Closure CreateRiskClosure(Closure closure);


    /// <summary>
    /// Checks if a closure already exists for a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public bool ClosureExists(int riskId);
    
    /// <summary>
    /// Deletes a risck closure
    /// </summary>
    /// <param name="closureId"></param>
    public void DeleteRiskClosure(int closureId);
    
    /// <summary>
    /// Get the list of risk impacts
    /// </summary>
    /// <returns>List of risk impacts</returns>
    public List<Impact> GetRiskImpacts();
    
    /// <summary>
    /// Gets the risk catalog item
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    RiskCatalog GetRiskCatalog(int id);
    
    
    /// <summary>
    ///  Gets the risk score value
    /// </summary>
    /// <param name="probabilityId"></param>
    /// <param name="impactId"></param>
    /// <returns></returns>
    public double GetRiskScore(int probabilityId, int impactId);

    /// <summary>
    /// Gets a list of risk catalogs
    /// </summary>
    /// <param name="ids"></param>
    /// <returns></returns>
    List<RiskCatalog> GetRiskCatalogs(List<int> ids);
    
    List<RiskCatalog> GetRiskCatalogs();
    
    /// <summary>
    /// Gets the risk source
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    Source GetRiskSource(int id);

    /// <summary>
    /// List the risk sources
    /// </summary>
    /// <returns></returns>
    List<Source> GetRiskSources();
    
    /// <summary>
    /// Gets all the risks that needs a mgmtReview
    /// </summary>
    /// <param name="status">Filter risk status</param>
    /// <returns>List of risks</returns>
    List<Risk> GetRisksNeedingReview(string? status = null);
    
    /// <summary>
    /// Gets the list of risk vulnerabilities
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    [Obsolete("Use GetVulnerabilitiesAsync instead")]
    List<Vulnerability> GetVulnerabilities(int riskId);
    
    /// <summary>
    /// Gets the list of risk vulnerabilities
    /// </summary>
    /// <param name="riskId"></param>
    /// <param name="includeClosed">Defines if closed vulnerabilities should be retrieved</param>
    /// <returns></returns>
    public Task<List<Vulnerability>> GetVulnerabilitiesAsync(int riskId, bool includeClosed = false);

    /// <summary>
    /// Gets the list of Incident Response Plans
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public Task<IncidentResponsePlan?> GetIncidentResponsePlanAsync(int riskId);
    
    /// <summary>
    /// Associate an existing risk to and existing incident response plan
    /// </summary>
    /// <param name="riskId"> the Id of the risk</param>
    /// <param name="incidentResponsePlanId">the Id of the incident response Plan</param>
    public Task AssocianteRiskToIncidentResponsePlanAsync(int riskId, int incidentResponsePlanId);
}