using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One synchronization run of one integration (Track 4).
///
/// Shared by the issue-tracker poller, Vision One, SecurityScorecard and SCIM so the operator has one
/// screen that answers "what ran and did it work" instead of four that answer it differently.
/// </summary>
public class IntegrationSyncLog
{
    public int Id { get; set; }

    public IntegrationKind Integration { get; set; }

    /// <summary>Id of the connection row within its own table. Not a foreign key — the table differs per integration.</summary>
    public int? ConnectionId { get; set; }

    /// <summary>Connection name captured at run time, so a deleted connection's history stays readable.</summary>
    public string? ConnectionName { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public IntegrationSyncStatus Status { get; set; } = IntegrationSyncStatus.Running;

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    /// <summary>Human summary of what happened, for the log list.</summary>
    public string? Summary { get; set; }

    /// <summary>Error text, redacted of credentials.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The run's progress trail: one <c>[HH:mm:ss] step: message</c> line per step, appended while the
    /// run is still going.
    ///
    /// It lives on this row rather than in a child table because the only question it answers is
    /// "what was this run doing", and that question is always asked about one run. It is written by
    /// <c>IntegrationSyncRun</c>, which buffers lines and flushes them in batches — a row rewrite per
    /// step would put thousands of UPDATEs behind a sync of a few thousand assets.
    ///
    /// Capped at <c>IntegrationSyncRun.MaxProgressLength</c>: a run that emits more than that keeps its
    /// beginning and its end and drops the middle, because the beginning says what it set out to do and
    /// the end says where it stopped, which is what a diagnosis needs.
    /// </summary>
    public string? ProgressLog { get; set; }
}
