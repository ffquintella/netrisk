using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A SecurityScorecard connection, scoped to one target domain (Track 4 milestone 4.5.1).
///
/// One row per domain rather than one per token: a holding company rates a dozen domains with the
/// same token, and each domain maps to a different NetRisk entity.
/// </summary>
public class SecurityScorecardConnection
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>The rated domain — <c>acme.com</c>. Part of nearly every request path.</summary>
    public string Domain { get; set; } = null!;

    /// <summary>API root. Overridable for the rare private deployment.</summary>
    public string BaseUrl { get; set; } = "https://api.securityscorecard.io";

    /// <summary>Encrypted API token, presented as <c>Authorization: Token &lt;key&gt;</c>.</summary>
    public string? EncryptedApiToken { get; set; }

    /// <summary>The business entity whose Cyber Risk Index this domain's score feeds.</summary>
    public int? EntityId { get; set; }

    public bool Enabled { get; set; } = true;

    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>Ingest <c>issues/potentially_vulnerable</c> CVEs as findings.</summary>
    public bool SyncVulnerabilities { get; set; } = true;

    /// <summary>Ingest active issues (missing SPF, expiring SSL, open ports) as findings.</summary>
    public bool SyncIssues { get; set; } = true;

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual ICollection<SecurityScorecardFactor> Factors { get; set; }
        = new List<SecurityScorecardFactor>();
}
