﻿using System.Collections.Generic;
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
    [Obsolete("Use GetVulnerabilitiesDistributionAsync instead")]
    List<ValueName> GetVulnerabilitiesDistribution();
    
    /// <summary>
    /// Gets a list representing the distribution of the vulnerabilities across the different risk levels.
    /// </summary>
    /// <returns></returns>
    public Task<List<ValueName>> GetVulnerabilitiesDistributionAsync();
    
    /// <summary>
    /// Gets the percentage of verified vulnerabilities
    /// </summary>
    /// <returns></returns>
    public Task<float> GetVulnerabilitiesVerifiedPercentageAsync();
    
    /// <summary>
    /// Gets the number of vulnerabilities per risk level.
    /// </summary>
    /// <returns></returns>
    public VulnerabilityNumbers GetVulnerabilityNumbers();
    
    
    /// <summary>
    /// Gets the numbers of vulnerabilities trough time
    /// </summary>
    /// <returns></returns>
    public Task<VulnerabilityNumbersByTime> GetVulnerabilityNumbersByTimeAsync(int daysSpan = 30);
    
    /// <summary>
    /// Gets the statistics of the risks
    /// </summary>
    /// <returns></returns>
    public Task<RisksNumbers> GetRisksNumbersAsync();
    
    /// <summary>
    /// Gets the top risks groups
    /// </summary>
    /// <returns></returns>
    public Task<List<RiskGroup>> GetRisksTopGroupsAsync();
    
    /// <summary>
    /// Gets the top risks entities
    /// </summary>
    /// <param name="count"></param>
    /// <param name="entityType"></param>
    /// <returns></returns>
    public Task<List<RiskEntity>> GetRisksTopEntitiesAsync(int count = 10, string? entityType = null);
    
    /// <summary>
    /// Gets the number of vulnerabilities per improt source.
    /// </summary>
    /// <returns></returns>
    public List<ValueName> GetVulnerabilityImportSources();
    
    /// <summary>
    /// Gets the number of vulnerabilities per status.
    /// </summary>
    /// <returns></returns>
    public Task<VulnerabilityNumbersByStatus> GetVulnerabilitiesNumbersByStatusAsync();
    
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