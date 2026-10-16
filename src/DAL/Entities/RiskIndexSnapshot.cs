using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One node of one day's official index, per category (S39 §9.1, §12.5) — <c>risk_index_snapshots</c>. Category 0 is
/// the headline, 1–6 the categories' <c>C_k</c>. Insert-only: written by the snapshot job, purged only by the retention
/// job, never audited. Entity-scoped on <see cref="EntityId"/> as defence in depth (S40 §7.4); no foreign key on the
/// entity, because the row is history and outlives it (S40 §7.1).
/// </summary>
public class RiskIndexSnapshot : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    /// <summary>The UTC day the snapshot belongs to.</summary>
    public DateOnly SnapshotDate { get; set; }

    public int ProfileId { get; set; }

    /// <summary>The profile's mode, copied: a snapshot whose mode differs from its profile's is refused by the job.</summary>
    public RiskIndexMode Mode { get; set; }

    public RiskIndexScopeKind ScopeKind { get; set; }

    /// <summary>The node within <see cref="ScopeKind"/>: an entity id, an asset class, a source id, or the root's key.</summary>
    public string ScopeRef { get; set; } = null!;

    /// <summary>The entity the node is, for the scope filter; null for the root, asset-class and source nodes.</summary>
    public int? EntityId { get; set; }

    public RiskIndexCategory Category { get; set; }

    /// <summary>ICR* (headline) or <c>C_k</c> (category), full precision.</summary>
    public double ValueUnfloored { get; set; }

    /// <summary>The headline with its display floor, full precision; equal to <see cref="ValueUnfloored"/> on a category.</summary>
    public double ValueFloored { get; set; }

    /// <summary>The integer shown, rounded from <see cref="ValueFloored"/> at full precision (S39 §4.5).</summary>
    public int ValueDisplayed { get; set; }

    /// <summary>JSON: the active floors and why. Null when none is active.</summary>
    public string? FloorReasons { get; set; }

    /// <summary>COV, in [0, 1] (S39 §8.4).</summary>
    public decimal Coverage { get; set; }

    /// <summary>Freshness, in [0, 1].</summary>
    public decimal Freshness { get; set; }

    /// <summary>Data quality, in [0, 1] (S39 §8.5 — not evidence confidence).</summary>
    public decimal Quality { get; set; }

    /// <summary>The ignorance interval's lower end (S39 §8.6), when shown.</summary>
    public double? IntervalLow { get; set; }

    /// <summary>The ignorance interval's upper end (S39 §8.6), when shown.</summary>
    public double? IntervalHigh { get; set; }

    public int ObjectCount { get; set; }

    /// <summary><c>C_k − 100·A(Aval_k)</c>: what the presumed objects add (S39 §4.3).</summary>
    public double PresumptionPoints { get; set; }

    /// <summary>SHA-256 (hex) of the external inputs the profile does not copy (S39 §9.5); a change is a marker.</summary>
    public string ExternalInputsHash { get; set; } = null!;

    /// <summary>JSON: the 50 largest contributions with the indicator sensitivity — only on the headline row (S40 §7.1).</summary>
    public string? TopContributions { get; set; }

    /// <summary>JSON: the change attribution against the previous day (S39 §9.4).</summary>
    public string? ChangeAttribution { get; set; }

    /// <summary>The headline row's display state (S39 §8.6); null on a category row. Added by T247 (schema 107).</summary>
    public RiskIndexDisplayState? DisplayState { get; set; }

    /// <summary>A category row's state (S39 §8.1); null on the headline row. Only an available category's value is a C_k.</summary>
    public RiskIndexCategoryState? CategoryState { get; set; }

    /// <summary>JSON, headline row only: the day's discontinuity markers of this node (S39 §9.5); null when none.</summary>
    public string? Markers { get; set; }

    /// <summary>JSON: the chips of the row (S39 §9.1 "pisos ativos e chips"; S40 §5.2.2) with their arguments; null when none.</summary>
    public string? Chips { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;
}
