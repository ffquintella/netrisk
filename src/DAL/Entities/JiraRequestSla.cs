namespace DAL.Entities;

/// <summary>
/// One SLA cycle of one metric on one mirrored request (Track 4 milestone 4.6).
///
/// Columns rather than a JSON blob on the request. "What is breaching this week", "which metric
/// breaches most often" and "how much time is left" are the only questions this data is ever asked,
/// and a blob answers none of them without a full scan and a parse per row.
///
/// A row per cycle, not per metric: Jira reports zero or more <c>completedCycles</c> and zero or one
/// <c>ongoingCycle</c> for each metric, and a request that is reopened starts a second cycle of the
/// same metric. Collapsing them would lose the first breach.
/// </summary>
public class JiraRequestSla
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    /// <summary>Jira's metric id, where it reports one.</summary>
    public string? MetricId { get; set; }

    /// <summary>The metric's display name — <c>Time to first response</c>.</summary>
    public string MetricName { get; set; } = null!;

    /// <summary>True for the <c>ongoingCycle</c>; false for a completed one.</summary>
    public bool IsOngoing { get; set; }

    public bool Breached { get; set; }

    /// <summary>The clock is stopped — pending the customer, or outside calendar hours.</summary>
    public bool Paused { get; set; }

    public long? GoalDurationMs { get; set; }

    public long? ElapsedMs { get; set; }

    /// <summary>Negative once the goal is passed, which is how Jira reports it.</summary>
    public long? RemainingMs { get; set; }

    public DateTime? CycleStartAt { get; set; }

    public DateTime? CycleStopAt { get; set; }

    /// <summary>When NetRisk read this, so a stale mirror is visible as stale.</summary>
    public DateTime CapturedAt { get; set; }

    public virtual JiraServiceRequest? Request { get; set; }
}
