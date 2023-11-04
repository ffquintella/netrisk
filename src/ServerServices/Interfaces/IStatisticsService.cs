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
    /// Gets the percentage of vulnerabilities that have been verified.
    /// </summary>
    /// <returns></returns>
    public float GetVulnerabilitiesVerifiedPercentage();
    
    
    /// <summary>
    /// Gets the number of vulnerabilities per risk level.
    /// </summary>
    /// <returns></returns>
    public VulnerabilityNumbers GetVulnerabilityNumbers();
}