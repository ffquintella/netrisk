namespace DAL.Entities;

/// <summary>
/// Idempotence guard for SLA notifications (Track 3 milestone 3.4.3).
///
/// One row per (finding, threshold) that has been notified. The daily job checks here before
/// sending, which is what makes "a finding crossing its due date triggers exactly one breach
/// notification" true even when the job runs twice, or is re-run after a failure.
/// </summary>
public class SlaNotification
{
    public int Id { get; set; }

    public int VulnerabilityId { get; set; }

    /// <summary>
    /// Days before the due date this notification was for; 0 means the breach itself. Negative
    /// values are not used — an overdue finding is notified once, at breach, and then appears in
    /// the overdue report rather than generating a fresh alert every day.
    /// </summary>
    public int ThresholdDays { get; set; }

    public DateTime NotifiedAt { get; set; }

    /// <summary>
    /// The due date the notification was computed against. A severity change moves the due date,
    /// and a finding whose deadline moved deserves a fresh warning — comparing this against the
    /// current due date is how the job tells "already notified" from "notified about a date that no
    /// longer applies".
    /// </summary>
    public DateTime DueDate { get; set; }

    /// <summary>Who was told. Null when the finding had no owner and the digest went to the fallback.</summary>
    public int? RecipientUserId { get; set; }

    public virtual Vulnerability? Vulnerability { get; set; }

    public virtual User? RecipientUser { get; set; }
}
