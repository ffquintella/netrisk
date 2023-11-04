﻿using Model.DTO.Statistics;
using Model.Statistics;

namespace ClientServices.Interfaces;

public interface IStatisticsService
{
    List<RisksOnDay> GetRisksOverTime();
    SecurityControlsStatistics GetSecurityControlStatistics();
    
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
    VulnerabilityNumbers GetVulnerabilityNumbers();

}