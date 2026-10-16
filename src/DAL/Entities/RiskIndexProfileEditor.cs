using System;

namespace DAL.Entities;

/// <summary>
/// Who wrote an index profile (S39 §13.1; S40 §6.4, §11.4) — <c>risk_index_profile_editors</c>: one row per user who created
/// or edited the draft, written in the same save as the edit. The approval's <c>sod_author_cannot_approve</c> and the shadow
/// pilot's "authors and editors read their draft" rule read it, never <c>audit_logs</c>, which the audit retention purges
/// and whose writer is best-effort. Not entity-scoped (profiles are the organization's configuration).
/// </summary>
public class RiskIndexProfileEditor
{
    public int Id { get; set; }

    public int ProfileId { get; set; }

    /// <summary>The editor. Null once the user is deleted (SET NULL: the evidence stays).</summary>
    public int? UserId { get; set; }

    /// <summary>UTC. The user's first edit of the profile.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. The user's latest edit of the profile.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;

    public virtual User? User { get; set; }
}
