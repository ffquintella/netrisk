using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IRisksService
{
    /// Returns the list of risks
    public List<Risk> GetAllRisks(bool includeClosed = false);
    
    public List<Risk> GetUserRisks();
    public string GetRiskCategory(int id);
    public RiskScoring GetRiskScoring(int id);
    public List<Category>? GetRiskCategories();
    public string GetRiskSource(int id);
    
    public Closure? GetRiskClosure(int riskId);

    public List<CloseReason> GetRiskCloseReasons();
    
    public List<Source>? GetRiskSources();
    
    public List<Likelihood>? GetProbabilities();
    
    public List<Impact>? GetImpacts();
    
    public float GetRiskScore(int probabilityId, int impactId);
    
    public List<RiskCatalog> GetRiskTypes(string ids,  bool all = false);
    
    public List<RiskCatalog> GetRiskTypes();
    
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
    /// Gets the last management review associated to a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public MgmtReview? GetRiskLastMgmtReview(int riskId);
    
}