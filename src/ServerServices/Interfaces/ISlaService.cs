using Contracts.Importers;
using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// SLA policy and due-date arithmetic (Track 3 milestone 3.4).
///
/// The service owns two things that must not drift apart: which policy applies to a finding, and
/// what due date that policy implies. Both are derived from effective-dated
/// <c>sla_configurations</c> rows so a policy change never rewrites a past compliance number.
/// </summary>
public interface ISlaService
{
    /// <summary>
    /// The policy in force for a severity at a moment, preferring an entity override over the
    /// global default. Null when no policy covers that severity — informational findings have none,
    /// and a finding with no policy simply has no due date.
    /// </summary>
    Task<SlaConfiguration?> ResolveAsync(NormalizedSeverity severity, int? entityId, DateTime atUtc);

    /// <summary>
    /// The remediation deadline for a finding first seen at <paramref name="firstSeenUtc"/>: that
    /// date plus the policy's remediation allowance. Null when no policy applies.
    /// </summary>
    Task<DateTime?> ComputeDueDateAsync(NormalizedSeverity severity, int? entityId, DateTime firstSeenUtc);

    /// <summary>Every policy row, current and superseded, for the admin view.</summary>
    Task<List<SlaConfiguration>> GetConfigurationsAsync(bool includeSuperseded = false);

    /// <summary>
    /// Supersedes the current policy for a severity/entity and inserts the new one, effective from
    /// <see cref="SlaConfiguration.EffectiveFrom"/> (defaulted to now). Never edits a row in place:
    /// that is what keeps historical compliance numbers reproducible.
    /// </summary>
    Task<SlaConfiguration> SetConfigurationAsync(SlaConfiguration configuration, int? userId);

    /// <summary>
    /// Recomputes and persists <c>sla_due_date</c> for one finding — called when its severity
    /// changes, which is the only thing that legitimately moves a deadline. Returns the new due date.
    /// </summary>
    Task<DateTime?> RecomputeDueDateAsync(int findingId, int? userId);

    /// <summary>
    /// The daily notification pass (3.4.3). Groups at-risk findings by owner, skips
    /// finding/threshold pairs already notified for the same due date, and returns one digest per
    /// recipient. Notifying is the caller's job; this decides who hears about what.
    /// </summary>
    Task<List<SlaDigest>> BuildNotificationDigestsAsync(DateTime nowUtc, int[]? approachingThresholdDays = null);

    /// <summary>
    /// Records that a digest was delivered, so the same finding/threshold is not notified again.
    /// Separate from building the digest so a send failure does not mark the notification as done.
    /// </summary>
    Task RecordNotificationsAsync(IEnumerable<SlaDigest> delivered, DateTime nowUtc);

    /// <summary>
    /// SLA compliance by severity over the currently open findings — the dashboard widget's data
    /// (3.4.2).
    /// </summary>
    Task<List<SlaComplianceBucket>> GetComplianceBySeverityAsync(DateTime nowUtc);
}

/// <summary>One recipient's at-risk findings for one pass.</summary>
public class SlaDigest
{
    /// <summary>Null when the findings have no owner; the caller routes those to the fallback address.</summary>
    public int? RecipientUserId { get; set; }

    public string? RecipientEmail { get; set; }

    public string? RecipientName { get; set; }

    public List<SlaDigestItem> Items { get; set; } = new();

    /// <summary>Findings already past their deadline. The part of the digest that leads.</summary>
    public IEnumerable<SlaDigestItem> Breached => Items.Where(i => i.ThresholdDays == 0);

    public IEnumerable<SlaDigestItem> Approaching => Items.Where(i => i.ThresholdDays > 0);
}

/// <summary>One finding in a digest, with the threshold it crossed.</summary>
public class SlaDigestItem
{
    public int FindingId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Severity { get; set; }

    public DateTime DueDate { get; set; }

    /// <summary>Days before the due date this alert is for; 0 means the deadline has passed.</summary>
    public int ThresholdDays { get; set; }

    public int DaysOverdue { get; set; }

    public string? AssetName { get; set; }
}

/// <summary>SLA compliance for one severity band.</summary>
public class SlaComplianceBucket
{
    public NormalizedSeverity Severity { get; set; }

    /// <summary>Open findings in this band that have a due date.</summary>
    public int Total { get; set; }

    public int WithinSla { get; set; }

    public int Breached { get; set; }

    /// <summary>
    /// Percentage within SLA, or null when the band has no findings — reporting 100% for an empty
    /// band reads as a result when it is an absence of data.
    /// </summary>
    public double? CompliancePercent => Total == 0 ? null : Math.Round(100.0 * WithinSla / Total, 1);
}
