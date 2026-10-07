using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A restoration test run against a business process or IT service (Stage 9.3, S43 §4.3), comparable
/// against the RTO/RPO its BIA declares.
///
/// Insert-only evidence. A mistaken record is voided — with a reason, an author and a time — and kept;
/// nothing edits or deletes one (S43 §11, D10). The void columns are the only change a row ever sees,
/// and they are their own timestamp, which is why there is no <c>updated_at</c>.
///
/// <see cref="DeclaredRtoMinutes"/> and <see cref="DeclaredRpoMinutes"/> copy the BIA as it stood when
/// the test was recorded. They are evidence only: verification compares the measure against the
/// objective declared <i>now</i>.
/// </summary>
public class RestorationTest
{
    public int Id { get; set; }

    public int EntityId { get; set; }

    /// <summary>UTC. When the test was run; never in the future.</summary>
    public DateTime TestedAt { get; set; }

    public RestorationTestOutcome Outcome { get; set; }

    /// <summary>The time it took to restore, measured.</summary>
    public int? AchievedRtoMinutes { get; set; }

    /// <summary>The window of data lost, measured.</summary>
    public int? AchievedRpoMinutes { get; set; }

    public int? DeclaredRtoMinutes { get; set; }

    public int? DeclaredRpoMinutes { get; set; }

    /// <summary>A ticket, minutes, a report — text, never opened as a link by the client.</summary>
    public string? EvidenceReference { get; set; }

    public string? Notes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? RecordedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? RecordedBy { get; set; }

    public virtual User? VoidedBy { get; set; }
}
