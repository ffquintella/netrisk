using System.Collections.Generic;
using DAL.Entities;
using Sieve.Models;

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
    public List<RiskScoring> GetRisksScoring(List<int> ids);
    
    /// <summary>
    /// Gets the list of risk scoring
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<List<RiskScoring>> GetRisksScoringAsync(List<int> ids);
    
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
    public Task<List<Risk>> GetAllAsync(string? status = null, string? notStatus = "Closed", bool includeCatalogs = true, System.Security.Claims.ClaimsPrincipal? userPrincipal = null);

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
    /// Saves a risk with the Track 8 milestone 8.3.1 state machine applied.
    ///
    /// The synchronous <see cref="SaveRisk"/> persists whatever status the client sends, which is the
    /// gap this closes: a risk could reach <c>Closed</c> with no management review, or sit in
    /// <c>Mitigation Planned</c> with no mitigation row. Throws
    /// <see cref="Model.Exceptions.InvalidStateTransitionException"/> when the transition is refused.
    /// </summary>
    Task SaveRiskAsync(Risk risk);
    
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
    /// Get the list of risk impacts
    /// </summary>
    /// <returns></returns>
    public Task<List<Impact>> GetRiskImpactsAsync();
    
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

    // --- Track 8 milestone 8.5.2: pending-risk triage -------------------------------------------

    /// <summary>
    /// The assessment-generated intake queue. Before Track 8 these rows accumulated and no code path
    /// ever promoted one to a risk, so the pipeline from an assessment answer to the register was
    /// dead. Defaults to the untriaged ones, which is what a triage screen wants.
    /// </summary>
    Task<List<Model.Governance.PendingRiskListing>> GetPendingRisksAsync(
        DAL.Enums.PendingRiskStatus? status = DAL.Enums.PendingRiskStatus.Pending);

    /// <summary>
    /// Promotes a pending risk into the register, carrying the assessment linkage for traceability.
    /// Refuses a row that has already been triaged — promoting twice would create two risks from one
    /// answer, and nothing would say they were the same finding.
    /// </summary>
    Task<Risk> PromotePendingRiskAsync(int pendingRiskId, Model.Governance.PendingRiskPromotion edits,
        int actingUserId);

    /// <summary>Drops a pending risk with a stated reason. The reason is mandatory.</summary>
    Task DismissPendingRiskAsync(int pendingRiskId, string reason, int actingUserId);

    // --- Track 8 milestone 8.5.1: event-triggered review ----------------------------------------

    /// <summary>
    /// Flags a risk as needing a review before its cadence would ask for one — DORA Art. 6(5)'s
    /// "after major incidents". Idempotent: flagging an already-flagged risk keeps the first reason
    /// and does not reset the clock.
    /// </summary>
    Task<bool> RequestReviewAsync(int riskId, string reason);

    /// <summary>Risks currently flagged for an out-of-cadence review.</summary>
    Task<List<Risk>> GetReviewRequestedAsync();

    // --- Track 8 milestone 8.2.2: both scores side by side --------------------------------------

    /// <summary>Inherent and residual score with the delta, for the lists, editors and reports.</summary>
    Task<List<Model.Governance.RiskScorePair>> GetScorePairsAsync(List<int>? riskIds = null);
    
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
    /// Gets the list of risk vulnerabilities with siev filter
    /// </summary>
    /// <param name="riskId"></param>
    /// <param name="filter"></param>
    /// <returns></returns>
    public Task<Tuple<int, List<Vulnerability>>> GetFilteredVulnerabilitiesAsync(int riskId, SieveModel filter);

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