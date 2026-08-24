using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Findings;

namespace ClientServices.Interfaces;

/// <summary>
/// The Track 3 administration surface the desktop client needs: deduplication heuristics per
/// scanner, SLA policy, risk acceptances, and CI API tokens.
///
/// One service rather than four because these four screens live together under Administration and
/// are always opened by the same person doing the same job; four rest services with identical
/// plumbing would be more files saying less.
/// </summary>
public interface IFindingsAdminService
{
    // --- 3.3.3 deduplication configuration --------------------------------------------------

    Task<List<ScannerDedupConfiguration>> GetDedupConfigurationsAsync();

    Task<ScannerDedupConfiguration> GetDedupConfigurationAsync(string importer);

    /// <summary>The strategies and hash fields a configuration may name, for the checkbox list.</summary>
    Task<DedupOptions> GetDedupOptionsAsync();

    Task<ScannerDedupConfiguration> SaveDedupConfigurationAsync(ScannerDedupConfiguration configuration);

    Task<List<ScannerDedupConfigurationHistory>> GetDedupHistoryAsync(string importer);

    /// <summary>
    /// Asks the server what two findings' dedup keys would be, and whether they would merge — the
    /// preview panel that makes a heuristic change reviewable before it is saved.
    /// </summary>
    Task<DedupPreviewResult> PreviewDedupAsync(string importer, PreviewFinding left, PreviewFinding right);

    // --- 3.4.1 SLA policy -------------------------------------------------------------------

    Task<List<SlaConfiguration>> GetSlaConfigurationsAsync(bool includeSuperseded = false);

    /// <summary>The benchmark values shown as guidance beside the form.</summary>
    Task<List<SlaBenchmarkView>> GetSlaBenchmarksAsync();

    Task<SlaConfiguration> SetSlaConfigurationAsync(SlaConfiguration configuration);

    // --- 3.2.3 risk acceptances -------------------------------------------------------------

    /// <summary><paramref name="expiringWithinDays"/> is the management view's headline filter.</summary>
    Task<List<RiskAcceptance>> GetAcceptancesAsync(int? expiringWithinDays = null);

    Task<RiskAcceptance> GetAcceptanceAsync(int id);

    Task<RiskAcceptance> CreateAcceptanceAsync(RiskAcceptance acceptance, List<int> findingIds);

    Task<RiskAcceptance> UpdateAcceptanceAsync(RiskAcceptance acceptance);

    Task<RiskAcceptance> AddFindingsToAcceptanceAsync(int acceptanceId, List<int> findingIds);

    Task<RiskAcceptance> RevokeAcceptanceAsync(int acceptanceId, string reason);

    // --- 3.5.1 API tokens -------------------------------------------------------------------

    Task<List<ApiTokenSummary>> GetApiTokensAsync(bool includeRevoked = false);

    Task<List<string>> GetApiTokenScopesAsync();

    /// <summary>
    /// Issues a token. The secret in the result is the only copy that will ever exist — the server
    /// stores only its hash and has no endpoint that can show it again.
    /// </summary>
    Task<IssuedApiToken> IssueApiTokenAsync(string name, string scopes, DateTime? expiresAt, int? entityId);

    Task<ApiTokenSummary> RevokeApiTokenAsync(int id);
}

/// <summary>What a dedup configuration may be built from.</summary>
public class DedupOptions
{
    public List<string> Strategies { get; set; } = new();

    public List<string> HashFields { get; set; } = new();

    public List<string> DefaultHashFields { get; set; } = new();
}

/// <summary>The preview verdict, with every candidate key so a surprising merge can be explained.</summary>
public class DedupPreviewResult
{
    public string? StrategyChain { get; set; }

    public string? HashFields { get; set; }

    public bool WouldMerge { get; set; }

    public List<DedupKeyEntry> LeftKeys { get; set; } = new();

    public List<DedupKeyEntry> RightKeys { get; set; } = new();

    public List<string> SharedKeys { get; set; } = new();
}

/// <summary>One strategy's key.</summary>
public class DedupKeyEntry
{
    public string Strategy { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}

/// <summary>Benchmark guidance shown beside the SLA form.</summary>
public class SlaBenchmarkView
{
    public int Severity { get; set; }

    public string SeverityName { get; set; } = string.Empty;

    public int TriageDays { get; set; }

    public int RemediationDays { get; set; }

    public string Source { get; set; } = string.Empty;
}

/// <summary>A token as listed. Carries no secret, because none is stored.</summary>
public class ApiTokenSummary
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public int? EntityId { get; set; }

    public string? UserName { get; set; }

    public bool IsUsable { get; set; }
}

/// <summary>
/// The subset of a normalized finding the preview form lets an operator type in.
///
/// A client-side shape rather than the SDK's <c>NormalizedFinding</c>: the desktop client has no
/// business referencing the plugin SDK, and these are the only fields any deduplication strategy
/// actually keys on. The server binds them onto the real type.
/// </summary>
public class PreviewFinding
{
    public string Tool { get; set; } = string.Empty;

    public string? RuleId { get; set; }

    public string? ToolUniqueId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string? Component { get; set; }

    public string? ComponentVersion { get; set; }

    /// <summary>Comma-separated CVE ids, as an operator would paste them.</summary>
    public string? Cves { get; set; }

    /// <summary>The normalized severity, 0-4.</summary>
    public int Severity { get; set; }

    /// <summary>The asset address, which the hash's <c>asset</c> field resolves from.</summary>
    public string? HostIp { get; set; }

    public string? Port { get; set; }
}
