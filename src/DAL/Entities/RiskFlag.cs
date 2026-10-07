using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One mandatory flag of one risk (Stage 9.5, S46 §4.2): the part an assessor <b>declared</b>, with the
/// reason, and the part the system <b>derived</b>, with its basis. The flag is set when either is.
///
/// A table of its own rather than eleven columns on <c>risks</c>: <c>PUT /Risks/{id}</c> copies the whole
/// payload over the stored row, so a client that does not know a column would clear it with no trail
/// (S46 D1). The row is created on the first declaration or derivation and never deleted by the
/// reconciliation — only with the risk — so the audit trail hangs off a stable id.
///
/// <see cref="Derived"/> and its companions are written only by <c>RiskFlagsService</c>'s reconciliation,
/// as the system actor; <see cref="Declared"/> only by a declaration or a withdrawal, with a reason.
/// </summary>
public class RiskFlag
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public RiskFlagCode Flag { get; set; }

    public bool Declared { get; set; }

    /// <summary>The written reason of the last declaration or withdrawal.</summary>
    public string? DeclaredReason { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? DeclaredAt { get; set; }

    public int? DeclaredById { get; set; }

    public bool Derived { get; set; }

    /// <summary>The basis in force, as text; null when <see cref="Derived"/> is false.</summary>
    public string? DerivedBasis { get; set; }

    /// <summary>Flag 4 only: the highest continuity-threat weight (S43 D16).</summary>
    public decimal? DerivedWeight { get; set; }

    /// <summary>UTC. When <see cref="Derived"/> last changed.</summary>
    public DateTime? DerivedChangedAt { get; set; }

    /// <summary>Why <see cref="Derived"/> last changed — "basis found" or "basis lost: …".</summary>
    public string? DerivedNote { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Declared or derived.</summary>
    public bool IsSet => Declared || Derived;

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? DeclaredBy { get; set; }
}
