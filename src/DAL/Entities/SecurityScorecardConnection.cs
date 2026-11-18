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

    /// <summary>
    /// How many days of score history the sync recovers, 0–365 (M57, T274, S40 §6.9); 0 turns the backfill off. Raising it
    /// later recovers only the days before <see cref="HistoryBackfilledFrom"/>.
    /// </summary>
    public int HistoryBackfillDays { get; set; } = 365;

    /// <summary>The earliest day (UTC, at 00:00) the history backfill has covered; null until it first runs.</summary>
    public DateTime? HistoryBackfilledFrom { get; set; }

    /// <summary>Keep each factor's <c>issue_summary[]</c> — the score impact per issue type (T274). On by default.</summary>
    public bool StoreIssueImpact { get; set; } = true;

    /// <summary>
    /// When the scheduled sync is next due (M57, T276, S39 D-13): the start of the next interval slot, never the end of the
    /// last run, so a 24-hour interval syncs every day. A manual sync does not move it.
    /// </summary>
    public DateTime? NextDueAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual ICollection<SecurityScorecardFactor> Factors { get; set; }
        = new List<SecurityScorecardFactor>();
}
