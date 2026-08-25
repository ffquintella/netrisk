using DAL.Entities;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// The SecurityScorecard REST calls NetRisk makes (Track 4 milestone 4.5).
///
/// Behind an interface for the same reason as the Vision One client: the parsing is the fragile part
/// and it has to be testable against captured payloads rather than against the live service.
/// </summary>
public interface ISecurityScorecardClient
{
    /// <summary>
    /// Token and domain check (4.5.1): a read of <c>/companies/{domain}</c>, which is the one call that
    /// proves both the token works and the domain is one this account can see.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(SecurityScorecardConnection connection, string? token,
        CancellationToken ct = default);

    /// <summary>The company's overall score and grade (4.5.2).</summary>
    Task<SecurityScorecardCompany?> GetCompanyAsync(SecurityScorecardConnection connection, string? token,
        CancellationToken ct = default);

    /// <summary>The ten factor scores (4.5.2).</summary>
    Task<List<SecurityScorecardFactorScore>> GetFactorsAsync(SecurityScorecardConnection connection,
        string? token, CancellationToken ct = default);

    /// <summary>CVEs detected on the domain's assets (4.5.3).</summary>
    Task<List<SecurityScorecardIssue>> GetVulnerabilitiesAsync(SecurityScorecardConnection connection,
        string? token, CancellationToken ct = default);

    /// <summary>Active configuration issues — SPF, SSL, open ports (4.5.3).</summary>
    Task<List<SecurityScorecardIssue>> GetIssuesAsync(SecurityScorecardConnection connection, string? token,
        CancellationToken ct = default);
}
