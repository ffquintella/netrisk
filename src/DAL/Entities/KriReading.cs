using System;

namespace DAL.Entities;

/// <summary>
/// One reading of a key risk indicator (Stage 9.8, S49 §4.2) — the indicator's history.
///
/// Insert-only, like a restoration test (S43 D10): a mistaken reading is voided with a reason, an author and a time,
/// and stays in the history, ignored by the evaluation (S49 D14). Nothing edits or deletes one.
/// </summary>
public class KriReading
{
    public int Id { get; set; }

    public int KriId { get; set; }

    public decimal Value { get; set; }

    /// <summary>UTC. When the value was true — not when it was typed. Never in the future.</summary>
    public DateTime ObservedAt { get; set; }

    /// <summary>The source reference: a report, a ticket. Text, never opened as a link by a client.</summary>
    public string? Note { get; set; }

    public int? RecordedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }

    public virtual Kri Kri { get; set; } = null!;

    public virtual User? RecordedBy { get; set; }

    public virtual User? VoidedBy { get; set; }
}
