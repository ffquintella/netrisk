using DAL.Entities;

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

}