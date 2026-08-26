using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One field-level change to a risk-governance record (Track 8 milestone 8.4.1).
///
/// The existing <c>audit</c> table stores JSON blobs of old/new values keyed by table name — useful
/// for forensics, useless for the question auditors actually ask: "who lowered this risk's impact
/// from 4 to 2, and when". One row per changed field, indexed by entity and time, is what makes that
/// answerable with a query instead of a Serilog grep.
///
/// The scope is an allowlist (risks, scorings, mitigations, reviews, acceptances, appetites,
/// mitigation tasks, campaign decisions), not every entity: a global trail on a vulnerability import
/// would write millions of rows nobody reads.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    /// <summary>CLR entity name, e.g. <c>Risk</c>. Stored rather than the table name so the trail
    /// survives the Track 6 table renames.</summary>
    public string EntityType { get; set; } = null!;

    public int EntityId { get; set; }

    /// <summary>The property that changed. Empty for a create/delete summary row.</summary>
    public string Field { get; set; } = null!;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public AuditLogAction Action { get; set; }

    /// <summary>
    /// Who did it. Null only when nothing was attributable, which the system-user convention is
    /// meant to make impossible — a background job writes as the system user rather than as nobody.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Who acted, as a name: a login for an API request, <c>system</c> for a background job, the
    /// console command name for a CLI write. Stored beside <see cref="UserId"/> rather than instead
    /// of it because a job has no user row to point at, and "attributable" must not degrade to NULL
    /// the moment the writer is not a person.
    /// </summary>
    public string Actor { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// Groups every row written by one <c>SaveChanges</c>, so a multi-field edit reads back as one
    /// action rather than six unrelated ones.
    /// </summary>
    public string? CorrelationId { get; set; }

    public virtual User? User { get; set; }
}
