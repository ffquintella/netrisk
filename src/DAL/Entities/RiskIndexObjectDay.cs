using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One scored object in one category on one day (S39 §9.1, §12.5; S40 §7.1) — <c>risk_index_object_days</c>: what a
/// 90-day replay of a new profile needs (S39 §9.5). Insert-only, purged after 400 days by the retention job alone, never
/// audited. Entity-scoped on <see cref="EntityId"/>, the <b>scope entity of the source row</b> (S40 §7.4); the entities
/// the object is attributed to are <see cref="Attribution"/>. No foreign key on either: the row is history.
/// </summary>
public class RiskIndexObjectDay : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    /// <summary>The UTC day the row belongs to.</summary>
    public DateOnly SnapshotDate { get; set; }

    public int ProfileId { get; set; }

    public RiskIndexObjectKind ObjectKind { get; set; }

    /// <summary>The object's identity within its kind (a risk id, a host id, a connection id, a vendor id).</summary>
    public string ObjectRef { get; set; } = null!;

    /// <summary>REG to TER; never the headline.</summary>
    public RiskIndexCategory Category { get; set; }

    public RiskIndexPopulation Population { get; set; }

    /// <summary><c>R_{o,k}</c>, in [0, 1].</summary>
    public double RValue { get; set; }

    /// <summary><c>w_o</c>: the criticality modulator times the campaign rank (S39 §4.3, §6.5).</summary>
    public decimal Weight { get; set; }

    /// <summary>The effective criticality, 1–5 (S39 §5).</summary>
    public sbyte Criticality { get; set; }

    /// <summary>Where the criticality came from (<c>manual</c>, <c>cmdb</c>, <c>visionone</c>, <c>tenable_acr</c>, <c>default</c>…).</summary>
    public string CriticalitySource { get; set; } = null!;

    /// <summary>Who declared the criticality, when a person did. No foreign key: the row outlives the user.</summary>
    public int? CriticalityAuthorId { get; set; }

    public RiskIndexObjectState State { get; set; }

    /// <summary>The scope entity of the source row (<c>risks.entity_id</c>, <c>hosts.entity_id</c>, …). Null: unassigned.</summary>
    public int? EntityId { get; set; }

    /// <summary>JSON: the ids of the entities the object is attributed to (S39 §10.2).</summary>
    public string Attribution { get; set; } = null!;

    /// <summary>JSON: per factor, its type, <c>x</c>, <c>v</c>, <c>u_f</c> and the reading's stamp.</summary>
    public string Factors { get; set; } = null!;

    /// <summary>JSON, scenarios only: impact level, rank, <c>n</c>, <c>r_res</c>, <c>r_inh</c>, T, delay, credit, <c>σ_ref</c>.</summary>
    public string? ScenarioInputs { get; set; }

    /// <summary>JSON: up to 50 normalized signals per factor with the flags used; null for an assessed-clean object.</summary>
    public string? Signals { get; set; }

    /// <summary>The asset class (S39 §5.4); null for objects that are not assets (scenarios, target entities).</summary>
    public RiskIndexAssetClass? AssetClass { get; set; }

    /// <summary>One bit per S39 §3.2 source with a reading on the object (S40 §7.1); the bit order is the engine's catalogue.</summary>
    public int SourceMask { get; set; }

    /// <summary>The host's environment by the profile's vocabulary: the term, <c>prod</c> or <c>none</c>; null for non-hosts.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>v_o</c>: the validity of the object's freshest reading, for <c>fresh_k</c> (S39 §8.5). This and the next three
    /// are what the engine reads besides R and the weight, so the previous day, the 90-day replay and a derived read are
    /// recomputed from the row alone (S39 §9.4–§9.5). Added by T247 (schema 107).
    /// </summary>
    public double Freshness { get; set; } = 1;

    /// <summary><c>e_o</c>: the object's effect on <c>cov_k</c> (S39 §8.4).</summary>
    public double CoverageEffect { get; set; } = 1;

    /// <summary><c>R_max,o</c>: the interval's upper end while presumed or not assessed (S39 §8.6).</summary>
    public double RMax { get; set; } = 1;

    /// <summary>The value the indicator sensitivity puts the object at (S39 §4.8).</summary>
    public double ClearedR { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;
}
