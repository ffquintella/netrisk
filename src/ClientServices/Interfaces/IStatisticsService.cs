using System.Collections.Generic;
using Model.DTO.Statistics;
using Model.Statistics;

namespace ClientServices.Interfaces;

public interface IStatisticsService
{
    /// <summary>
    /// Gets the risks over time
    /// </summary>
    /// <returns></returns>
    List<RisksOnDay> GetRisksOverTime();
    
    /// <summary>
    /// Get the security controls statistics
    /// </summary>
    /// <returns></returns>
    public Task<List<RisksOnDay>> GetRisksOverTimeAsync();
    
    /// <summary>
    /// Get the security controls statistics
    /// </summary>
    /// <returns></returns>
    SecurityControlsStatistics GetSecurityControlStatistics();
    
    /// <summary>
    /// Get the security controls statistics
    /// </summary>
    /// <returns></returns>
    public Task<SecurityControlsStatistics> GetSecurityControlStatisticsAsync();

    /// <summary>
    /// Gets the vulnerability values by severity by import date.
    /// </summary>
    /// <returns></returns>
    public Task<List<ImportSeverity>> GetVulnerabilitiesServerityByImportAsync();
    
    /// <summary>
    /// Gets the list of Labeled points for the risks vs costs graph
    /// </summary>
    /// <returns></returns>
    List<LabeledPoints> GetRisksVsCosts(double minRisk, double maxRisk);
    
    /// <summary>
    /// Gets a list representing the distribution of the vulnerabilities across the different risk levels.
    /// </summary>
    /// <returns></returns>
    List<ValueName> GetVulnerabilitiesDistribution();
    
    /// <summary>
    /// Gets the percentage of verified vulnerabilities
    /// </summary>
    /// <returns></returns>
    float GetVulnerabilitiesVerifiedPercentage();
    
    /// <summary>
    /// Gets the number of vulnerabilities per risk level.
    /// </summary>
    /// <returns></returns>
    public VulnerabilityNumbers GetVulnerabilityNumbers();
    
    
    /// <summary>
    /// Gets the number of vulnerabilities per improt source.
    /// </summary>
    /// <returns></returns>
    public List<ValueName> GetVulnerabilityImportSources();
    
    /// <summary>
    /// Gets the number of vulnerabilities per status.
    /// </summary>
    /// <returns></returns>
    public VulnerabilityNumbersByStatus GetVulnerabilitiesNumbersByStatus();
    
    /// <summary>
    /// Gets the risks impact vs probability statistics
    /// </summary>
    /// <param name="minRisk"></param>
    /// <param name="maxRisk"></param>
    /// <returns></returns>
    public List<LabeledPoints> GetRisksImpactVsProbability(double minRisk, double maxRisk);
    
    /// <summary>
    /// Gets the entities risk values
    /// </summary>
    /// <returns></returns>
    public List<ValueNameType> GetEntitiesRiskValues(int? parentId = null, int topCount = 10);

}