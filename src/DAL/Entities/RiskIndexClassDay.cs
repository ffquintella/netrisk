using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The daily <c>C_k</c> of a pre-computed node intersected with an asset-class tab (S40 §5.5.1, §7.2) —
/// <c>risk_index_class_days</c>: the "official derived" value the monthly class summary reads, computed once by the job
/// instead of re-scanning the object rows on every read. Only categories available in the intersection are stored.
/// Insert-only, purged after 400 days by the retention job. Entity-scoped on <see cref="NodeEntityId"/>; the root's rows
/// (null) are read only with unrestricted scope.
/// </summary>
public class RiskIndexClassDay : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    /// <summary>The UTC day the value belongs to.</summary>
    public DateOnly SnapshotDate { get; set; }

    public int ProfileId { get; set; }

    /// <summary>The node's entity; null is the root. No foreign key: the row outlives the entity.</summary>
    public int? NodeEntityId { get; set; }

    public RiskIndexTabGroup TabGroup { get; set; }

    /// <summary>EXP, GOV or CTL — the categories an asset-class tab carries; never the headline.</summary>
    public RiskIndexCategory Category { get; set; }

    /// <summary><c>C_k</c> of the intersection, in [0, 100], full precision.</summary>
    public double Value { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;

    /// <summary>The write guard reads the node's entity: the column the scope filter reads.</summary>
    int? Interfaces.IEntityScoped.EntityId
    {
        get => NodeEntityId;
        set => NodeEntityId = value;
    }
}
