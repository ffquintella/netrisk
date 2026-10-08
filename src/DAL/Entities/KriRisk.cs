using System;

namespace DAL.Entities;

/// <summary>
/// A risk a key risk indicator governs (Stage 9.8, S49 §4.3): the KRI enters the risk's Gate B, and its breach triggers
/// the risk's reassessment. The KRI must be organization-wide or of the risk's own entity (S49 D6). Unlinking removes a
/// gate, so it is audited like linking.
/// </summary>
public class KriRisk
{
    public int Id { get; set; }

    public int KriId { get; set; }

    public int RiskId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Kri Kri { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}
