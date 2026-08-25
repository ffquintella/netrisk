using DAL.Enums;

namespace Model.Integrations;

/// <summary>
/// A SecurityScorecard connection as a client sees it (Track 4 milestone 4.5.1). The API token is
/// never returned — only whether one is set.
/// </summary>
public class SecurityScorecardConnectionView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public bool HasApiToken { get; set; }

    public int? EntityId { get; set; }

    public bool Enabled { get; set; }

    public int SyncIntervalHours { get; set; }

    public bool SyncVulnerabilities { get; set; }

    public bool SyncIssues { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    public string? LastSyncError { get; set; }
}

/// <summary>
/// The ten risk factors SecurityScorecard rates (Track 4 milestone 4.5.2).
///
/// Listed so the UI can render a row per factor even before a sync has produced one — a trend chart
/// that only shows the factors that happened to come back looks like data loss.
/// </summary>
public static class SecurityScorecardFactors
{
    public static readonly IReadOnlyList<string> All =
    [
        "network_security",
        "dns_health",
        "patching_cadence",
        "endpoint_security",
        "ip_reputation",
        "application_security",
        "cubit_score",
        "hacker_chatter",
        "leaked_information",
        "social_engineering"
    ];

    /// <summary>Turns <c>patching_cadence</c> into "Patching Cadence" for display.</summary>
    public static string Humanize(string factorName) =>
        string.Join(" ", factorName.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0
                ? part
                : char.ToUpperInvariant(part[0]) + part[1..]));
}

/// <summary>A company's overall rating (Track 4 milestone 4.5.2).</summary>
public class SecurityScorecardCompany
{
    public string Domain { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>0–100, where higher is better — the inverse of Vision One's convention.</summary>
    public int? Score { get; set; }

    /// <summary>Letter grade A–F.</summary>
    public string? Grade { get; set; }

    public string? Industry { get; set; }

    public int? Size { get; set; }

    public DateTime? LastSeen { get; set; }
}

/// <summary>One of the ten factor scores (Track 4 milestone 4.5.2).</summary>
public class SecurityScorecardFactorScore
{
    public string Name { get; set; } = string.Empty;

    public int Score { get; set; }

    public string? Grade { get; set; }

    public int? IssueCount { get; set; }
}

/// <summary>
/// One active issue from <c>/companies/{domain}/issues</c> — a missing SPF record, an expiring
/// certificate, an exposed port (Track 4 milestone 4.5.3).
/// </summary>
public class SecurityScorecardIssue
{
    /// <summary>The issue type's machine name, e.g. <c>spf_record_missing</c>.</summary>
    public string Type { get; set; } = string.Empty;

    public string? Severity { get; set; }

    /// <summary>Which of the ten factors this issue counts against.</summary>
    public string? FactorName { get; set; }

    /// <summary>The affected host, URL or IP as SecurityScorecard reports it.</summary>
    public string? Target { get; set; }

    public string? Description { get; set; }

    /// <summary>CVE id when the issue is a vulnerability rather than a configuration finding.</summary>
    public string? CveId { get; set; }

    public double? CvssScore { get; set; }

    public string? Port { get; set; }

    public DateTime? FirstSeen { get; set; }

    public DateTime? LastSeen { get; set; }

    /// <summary>Whether this row came from the CVE endpoint rather than the general issues endpoint.</summary>
    public bool IsVulnerability { get; set; }
}
