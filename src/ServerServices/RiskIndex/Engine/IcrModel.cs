using DAL.Enums;

namespace ServerServices.RiskIndex.Engine;

/// <summary>The identity of a scored object (S39 §2.1): its kind and its reference (a row id, or a group key).</summary>
public readonly record struct IcrObjectKey(RiskIndexObjectKind Kind, string Ref)
{
    public override string ToString() => $"{Kind}:{Ref}";
}

/// <summary>
/// Where an object stands in the category formula (S39 §4.3, §8.1): in <c>Aval_k</c> (its value enters <c>M_p</c>),
/// in <c>Pres_k</c> (it enters only by conservative inclusion), or only in the inventory (it counts in the coverage
/// denominator and nowhere else — GOV's host with an open finding and no SLA date).
/// </summary>
public enum IcrMembership
{
    Assessed = 1,
    Presumed = 2,
    InventoryOnly = 3
}

/// <summary>How a category stands in a node (S39 §8.1).</summary>
public enum IcrCategoryStatus
{
    /// <summary>In <c>K(S)</c>: weighted, with an enabled source and something assessed or presumed.</summary>
    Available = 1,

    /// <summary>Weighted and with inventory, but no enabled source, no intake, or nothing assessed or presumed: out of
    /// <c>K(S)</c> with renormalization, but in the coverage denominator with <c>cov_k = 0</c>.</summary>
    Unavailable = 2,

    /// <summary>No inventory in the node (REG and AME in an asset-class node): out of <c>K(S)</c> and of the coverage.</summary>
    NotApplicable = 3,

    /// <summary>Weight 0 in the profile (TER until M48).</summary>
    Disabled = 4
}

/// <summary>What the dashboard may show for a node, by coverage (S39 §8.6).</summary>
public enum IcrDisplayState
{
    /// <summary>COV ≥ <c>cov_ponto</c> and no category unavailable: value and band, interval in the detail.</summary>
    Point = 1,

    /// <summary>Lower coverage, or a weighted category unavailable: value, band and interval.</summary>
    WithInterval = 2,

    /// <summary>COV &lt; <c>cov_indicativo</c>: "indicative ICR" — value and interval, no band, no colour.</summary>
    Indicative = 3,

    /// <summary><c>K(S)</c> is empty.</summary>
    NoData = 4
}

/// <summary>
/// One object in one category as the category formula needs it (S39 §4.2–§4.3, §8.1–§8.6). <see cref="R"/> and
/// <see cref="Weight"/> are the object's own value and weight, independent of the node (S39 §4.6 "reconcilable").
/// </summary>
public sealed record IcrCategoryObject
{
    public required IcrObjectKey Key { get; init; }

    public required RiskIndexPopulation Population { get; init; }

    /// <summary><c>R_{o,k}</c> in [0, 1]; for a presumed object, <c>R_pres</c>.</summary>
    public double R { get; init; }

    /// <summary><c>w_o</c>: <c>m(crit)</c> for a technical object, <c>w_r = m(impact)·m_rank</c> for a scenario.</summary>
    public double Weight { get; init; }

    public IcrMembership Membership { get; init; } = IcrMembership.Assessed;

    /// <summary><c>v_o</c> for <c>fresh_k</c> (S39 §8.5): the validity of the object's freshest reading.</summary>
    public double Freshness { get; init; } = 1;

    /// <summary><c>e_o</c> for <c>cov_k</c> (S39 §8.4): 0 for a scenario without an assessed treatment; <c>1 − u_A/Σu</c>
    /// for a GOV scenario with no applicable appetite ceiling; 1 otherwise.</summary>
    public double CoverageEffect { get; init; } = 1;

    /// <summary><c>R_max,o</c> (S39 §8.6): the highest value the object could reach in the category, used as the upper
    /// end of the ignorance interval while it is presumed or not assessed.</summary>
    public double RMax { get; init; } = 1;

    /// <summary>The value the indicator sensitivity puts the object at (S39 §4.8): 0 (assessed clean) by default; an
    /// event object (AME) uses its value one half-life after closure.</summary>
    public double ClearedR { get; init; }
}

/// <summary>One category of one node, as the engine receives it.</summary>
public sealed record IcrCategoryInput
{
    public required RiskIndexCategory Category { get; init; }

    public IReadOnlyList<IcrCategoryObject> Objects { get; init; } = [];

    /// <summary>Some source of the category is enabled in the profile (AME: incident intake active). False makes a
    /// category with inventory <see cref="IcrCategoryStatus.Unavailable"/>.</summary>
    public bool SourceEnabled { get; init; } = true;

    /// <summary>The category is unavailable at this node by construction (interim EXP of an activity, S39 §10.1).</summary>
    public bool ForcedUnavailable { get; init; }

    /// <summary>Why the category is unavailable or partial, for the chip ("requires M39", "no incident intake").</summary>
    public string? Note { get; init; }

    /// <summary>Only part of the category's objects are linkable at this node (interim EXP of a process, S39 §10.1).</summary>
    public bool Partial { get; init; }
}

/// <summary>Everything the headline formula needs about one node (S39 §4.4, §8.4).</summary>
public sealed record IcrNodeInput
{
    public IReadOnlyList<IcrCategoryInput> Categories { get; init; } = [];

    /// <summary>Σ <c>m(crit)</c> of the critical processes (declared criticality ≥ 4) in the node's closure, and of those
    /// with at least one open scenario — <c>cov_processos</c> (S39 §8.4). Both 0 when no process is declared critical.</summary>
    public double CriticalProcessWeight { get; init; }

    public double CriticalProcessWeightCovered { get; init; }

    /// <summary>A Gate A display floor applies (S39 §7). Never in interim mode; the predicate is M43's.</summary>
    public bool GateAFloorTriggered { get; init; }
}

/// <summary>One object's share of a category value (S39 §4.8), in points of <c>C_k</c>.</summary>
public sealed record IcrObjectPart(IcrObjectKey Key, double Part, bool Presumed, bool HoldsFloor);

/// <summary>A category's value and everything published beside it (S39 §4.3, §8.4–§8.6, §9.1).</summary>
public sealed record IcrCategoryResult
{
    public required RiskIndexCategory Category { get; init; }
    public required IcrCategoryStatus Status { get; init; }

    /// <summary><c>C_k</c> in [0, 100], full precision; 0 when not available.</summary>
    public double Value { get; init; }

    /// <summary><c>100·A(Aval_k)</c>: the value with no presumed object.</summary>
    public double AssessedValue { get; init; }

    /// <summary>The presumption points <c>C_k − 100·A(Aval_k)</c> (S39 §4.3).</summary>
    public double PresumptionPoints => Value - AssessedValue;

    /// <summary><c>100·M_p</c> over the chosen set, and <c>100·τ·max R</c>.</summary>
    public double PowerMean { get; init; }

    public double FloorValue { get; init; }

    /// <summary>The tail floor sets the value: the dashboard says "held by" the <see cref="HeldBy"/> objects.</summary>
    public bool FloorActive { get; init; }

    public IReadOnlyList<IcrObjectKey> HeldBy { get; init; } = [];

    /// <summary>How many presumed objects the conservative inclusion took, in decreasing R order.</summary>
    public int PresumedIncluded { get; init; }

    /// <summary>Objects in the value set (assessed plus the included presumed ones), and in the inventory.</summary>
    public int ObjectCount { get; init; }

    public int InventoryCount { get; init; }

    /// <summary><c>cov_k</c> (for REG, times <c>cov_processos</c>) and <c>fresh_k</c>, in [0, 1].</summary>
    public double Coverage { get; init; }

    public double Freshness { get; init; } = 1;

    /// <summary>The category's ignorance interval, presumed and not-assessed objects at 0 and at <c>R_max</c>.</summary>
    public double IntervalLow { get; init; }

    public double IntervalHigh { get; init; }

    public bool Partial { get; init; }

    public string? Note { get; init; }

    /// <summary>The Euler parts of <see cref="Value"/>, summing to it exactly.</summary>
    public IReadOnlyList<IcrObjectPart> Parts { get; init; } = [];
}

/// <summary>One object's contribution to the headline (S39 §4.8): <c>g_k·part_o</c>, in points of ICR*.</summary>
public sealed record IcrContribution(IcrObjectKey Key, RiskIndexCategory Category, double Points, bool Presumed,
    bool HoldsFloor);

/// <summary>The indicator sensitivity of one object in one category (S39 §4.8): ICR*(object cleared) − ICR*.</summary>
public sealed record IcrSensitivity(IcrObjectKey Key, RiskIndexCategory Category, double ChangeIfCleared,
    IReadOnlyList<IcrObjectKey> NextHolders);

/// <summary>The headline of a node and its composition (S39 §4.4–§4.8, §8.4–§8.6).</summary>
public sealed record IcrNodeResult
{
    /// <summary>ICR*: the unfloored headline, full precision; null when <c>K(S)</c> is empty.</summary>
    public double? Unfloored { get; init; }

    /// <summary>ICR: with the Gate A display floor (equal to ICR* in interim mode).</summary>
    public double? Floored { get; init; }

    /// <summary>The displayed integer, from <see cref="Floored"/> in full precision.</summary>
    public int? Displayed { get; init; }

    /// <summary>The band of <see cref="Displayed"/> (index into the profile's <c>faixas</c>), or -1.</summary>
    public int BandIndex { get; init; } = -1;

    /// <summary>The weighted-mean branch and the headline floor <c>τ_G·max C_k</c>, over <c>K(S)</c>.</summary>
    public double WeightedMean { get; init; }

    public double HeadlineFloor { get; init; }

    public bool HeadlineFloorActive { get; init; }

    public double Coverage { get; init; }
    public double Freshness { get; init; } = 1;
    public double Quality { get; init; }
    public double? IntervalLow { get; init; }
    public double? IntervalHigh { get; init; }
    public IcrDisplayState DisplayState { get; init; }

    /// <summary>Distinct objects in the node, over every category.</summary>
    public int ObjectCount { get; init; }

    /// <summary>Σ presumption points of the categories, weighted as they enter the headline.</summary>
    public double PresumptionPoints { get; init; }

    /// <summary><c>cov_processos</c> had no critical process to measure ("no process declared critical").</summary>
    public bool NoCriticalProcess { get; init; }

    public IReadOnlyList<IcrCategoryResult> Categories { get; init; } = [];

    /// <summary>Each category's share of the headline, <c>g_k·C_k</c> (sums to ICR* exactly).</summary>
    public IReadOnlyDictionary<RiskIndexCategory, double> CategoryContributions { get; init; } =
        new Dictionary<RiskIndexCategory, double>();

    /// <summary>The largest object contributions, by points, at most <c>top_contribuicoes</c>.</summary>
    public IReadOnlyList<IcrContribution> TopContributions { get; init; } = [];

    /// <summary>The indicator sensitivity of the objects with the largest participation.</summary>
    public IReadOnlyList<IcrSensitivity> Sensitivity { get; init; } = [];
}
