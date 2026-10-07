using System;

namespace DAL.Entities;

/// <summary>
/// "This treatment cannot be delivered before that one" (Stage 9.6, S47 §4.2) — the dependency constraint of
/// Gate D. Across risks on purpose: "MFA depends on the IdP upgrade" is the common case. A cycle is refused
/// when written (S47 D8); Gate D still detects one, so a row planted around the service cannot loop it.
/// </summary>
public class MitigationDependency
{
    public int Id { get; set; }

    /// <summary>The dependent mitigation.</summary>
    public int MitigationId { get; set; }

    /// <summary>The mitigation that has to be delivered first.</summary>
    public int PrerequisiteId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Mitigation Mitigation { get; set; } = null!;

    public virtual Mitigation Prerequisite { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}
