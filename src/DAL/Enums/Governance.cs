namespace DAL.Enums;

/// <summary>
/// What a mitigation task line item is doing (Track 8 milestone 8.5.3). POA&amp;M-shaped: a task is
/// open, being worked, done, or explicitly abandoned — the last one matters because a plan of action
/// whose items silently vanish is not evidence of anything.
/// </summary>
public enum MitigationTaskStatus
{
    Open = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>
/// Triage state of an assessment-generated <c>pending_risks</c> row (Track 8 milestone 8.5.2).
///
/// Before this track the table had no state at all and nothing ever read it — rows accumulated and
/// no code path promoted one to a risk. The state is what makes the queue drainable.
/// </summary>
public enum PendingRiskStatus
{
    Pending = 1,
    Promoted = 2,
    Dismissed = 3
}

/// <summary>Lifecycle of a periodic business review campaign (Track 8 milestone 8.6.3).</summary>
public enum RiskReviewCampaignStatus
{
    Open = 1,
    Completed = 2,
    Overdue = 3,
    Cancelled = 4
}

/// <summary>
/// What the business reviewer decided about one risk in a campaign (Track 8 milestone 8.6.4).
/// </summary>
public enum RiskReviewDecision
{
    Pending = 1,
    Accepted = 2,
    MitigationRequested = 3,
    Escalated = 4
}

/// <summary>The kind of change an <c>audit_logs</c> row records (Track 8 milestone 8.4.1).</summary>
public enum AuditLogAction
{
    Create = 1,
    Update = 2,
    Delete = 3
}
