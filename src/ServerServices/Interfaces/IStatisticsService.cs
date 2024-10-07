using Model.Statistics;

namespace ServerServices.Interfaces;

public interface IStatisticsService
{
    /// <summary>
    /// Gets the points representing the risk score on one axes and the cost to mitigate on the other.
    /// </summary>
    /// <returns></returns>
    public List<LabeledPoints> GetRisksVsCosts(double minRisk, double maxRisk);

    /// <summary>
    /// Gets a list representing the distribution of the vulnerabilities across the different risk levels.
    /// </summary>
    /// <returns></returns>
    public List<ValueName> GetVulnerabilitiesDistribution();

    /// <summary>
    /// Gets the number of risk per day.
    /// </summary>
    /// <returns></returns>
    public List<RisksOnDay> GetRisksOverTime(int daysSpan = 30);
    
    /// <summary>
    /// Gets the vulnerability values by severity by import date.
    /// </summary>
    /// <param name="itemCount"></param>
    /// <returns></returns>
    public Task<List<ImportSeverity>> GetVulnerabilitiesServerityByImportAsync(int itemCount = 30);
    
    
    /// <summary>
    /// Gets the percentage of vulnerabilities that have been verified.
    /// </summary>
    /// <returns></returns>
    public float GetVulnerabilitiesVerifiedPercentage();
    
    
    /// <summary>
    /// Gets the number of vulnerabilities per risk level.
    /// </summary>
    /// <returns></returns>
    public VulnerabilityNumbers GetVulnerabilityNumbers();
    
    /// <summary>
    /// Gets the number of vulnerabilities per source.
    /// </summary>
    /// <returns></returns>
    public List<ValueName> GetVulnerabilitySources();


    /// <summary>
    /// Gets the number of vulnerabilities per status.
    /// </summary>
    /// <returns></returns>
    public VulnerabilityNumbersByStatus GetVulnerabilitiesNumbersByStatus();
    
    
    /// <summary>
    /// Gets the risk impact vs probability.
    /// </summary>
    /// <param name="minRisk"></param>
    /// <param name="maxRisk"></param>
    /// <returns></returns>
    public List<LabeledPoints> GetRisksImpactVsProbability(double minRisk, double maxRisk);
    
    /// <summary>
    ///Gets the risk values for all the entities. 
    /// </summary>
    /// <returns></returns>
    public List<ValueNameType> GetEntitiesRiskValues(int? parentId = null, int topCount = 10);
}