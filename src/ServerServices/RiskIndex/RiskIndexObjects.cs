using DAL.Enums;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex;

/// <summary>The kind of a factor (S39 §2.3): likelihood (V, modulated by criticality), composite (C) or scenario-scaled (S).</summary>
public enum RiskIndexFactorType
{
    V = 1,
    C = 2,
    S = 3
}

/// <summary>One factor reading of an object (S39 §9.1 "por fator: tipo, x, v, u_f e carimbo da leitura").</summary>
public sealed record RiskIndexFactorReading
{
    public required string Factor { get; init; }
    public RiskIndexFactorType Type { get; init; }

    /// <summary><c>x_{o,f}</c> before criticality and staleness.</summary>
    public double X { get; init; }

    /// <summary><c>v_f</c>, the reading's validity; 1 for state read at every snapshot.</summary>
    public double Validity { get; init; } = 1;

    public double Ceiling { get; init; } = 1;

    /// <summary>When the source last read the object; null for state read at every snapshot.</summary>
    public DateTime? ReadAt { get; init; }

    /// <summary>The S39 §3.2 source of the reading (<see cref="RiskIndexSources"/>).</summary>
    public string? Source { get; init; }
}

/// <summary>One normalized signal behind a factor (S39 §9.1: up to 50 per factor, only those with s &gt; 0).</summary>
public sealed record RiskIndexSignalReading(string Factor, string Id, double Value, IReadOnlyList<string> Flags);

/// <summary>A scenario's register inputs as the object row stores them (S39 §9.1, §9.5 replay).</summary>
public sealed record RiskIndexScenarioInputs
{
    public int? ImpactLevel { get; init; }
    public int? Rank { get; init; }
    public int? RankedItems { get; init; }
    public double? Residual { get; init; }
    public double Inherent { get; init; }
    public int CadenceDays { get; init; }
    public double DelayDays { get; init; }
    public double Credit { get; init; } = 1;
    public double EffectiveResidual { get; init; }
    public double SigmaRef { get; init; }
    public DateTime ReferenceDate { get; init; }
    public DateTime Due { get; init; }
    public bool TreatmentAssessed { get; init; }
    public bool ResidualAboveInherent { get; init; }
    public bool AppetiteConfigured { get; init; }
    public bool AboveCeiling { get; init; }
    public bool AcceptedAboveCeiling { get; init; }
    public bool AcceptanceExpiringSoon { get; init; }

    /// <summary>The first day of GOV.G's current run of lit days (S39 §6.3), carried to tomorrow for the latch.</summary>
    public DateTime? DivergenceSince { get; init; }
}

/// <summary>
/// One object in one category on one day — the content of a <c>risk_index_object_days</c> row (S39 §9.1, §12.5; S40
/// §7.1), plus what the engine needs. <see cref="ToEngine"/> is the only bridge to the formulas.
/// </summary>
public sealed record RiskIndexObjectRow
{
    public required IcrObjectKey Key { get; init; }
    public required RiskIndexCategory Category { get; init; }
    public required RiskIndexPopulation Population { get; init; }
    public double R { get; init; }
    public double Weight { get; init; }
    public int Criticality { get; init; }

    /// <summary><c>manual</c>, <c>cmdb</c>, <c>visionone</c>, <c>entity</c>, <c>impact</c>, <c>external</c>, <c>default</c> or <c>unknown</c>.</summary>
    public string CriticalitySource { get; init; } = "default";

    public int? CriticalityAuthorId { get; init; }
    public RiskIndexObjectState State { get; init; }

    /// <summary>The scope entity of the source row (S40 §7.4): the object row's <c>entity_id</c>.</summary>
    public int? ScopeEntityId { get; init; }

    /// <summary>The entities the object is attributed to (S39 §10.2); empty files it under Unassigned.</summary>
    public IReadOnlyList<int> Attribution { get; init; } = [];

    public IReadOnlyList<RiskIndexFactorReading> Factors { get; init; } = [];
    public RiskIndexScenarioInputs? ScenarioInputs { get; init; }
    public IReadOnlyList<RiskIndexSignalReading> Signals { get; init; } = [];
    public RiskIndexAssetClass? AssetClass { get; init; }

    /// <summary>One bit per source with a reading on the object (<see cref="RiskIndexSourceBits"/>).</summary>
    public int SourceMask { get; init; }

    /// <summary>The host's environment by the profile vocabulary: the term, <c>prod</c> or <c>none</c>; null if not a host.</summary>
    public string? Environment { get; init; }

    /// <summary><c>v_o</c>, <c>e_o</c>, <c>R_max</c> and the cleared value, for coverage, freshness, interval and sensitivity.</summary>
    public double Freshness { get; init; } = 1;

    public double CoverageEffect { get; init; } = 1;
    public double RMax { get; init; } = 1;
    public double ClearedR { get; init; }

    public IcrMembership Membership => State switch
    {
        RiskIndexObjectState.Presumed => IcrMembership.Presumed,
        RiskIndexObjectState.NotAssessed when Category != RiskIndexCategory.Reg => IcrMembership.InventoryOnly,
        _ => IcrMembership.Assessed
    };

    public IcrCategoryObject ToEngine() => new()
    {
        Key = Key, Population = Population, R = R, Weight = Weight, Membership = Membership, Freshness = Freshness,
        CoverageEffect = State == RiskIndexObjectState.NotAssessed && Category == RiskIndexCategory.Reg ? 0 : CoverageEffect,
        RMax = RMax, ClearedR = ClearedR
    };
}

/// <summary>
/// The bit of each source in <c>risk_index_object_days.source_mask</c> (S40 §7.1). Stored history: positions are
/// append-only, never reordered.
/// </summary>
public static class RiskIndexSourceBits
{
    private static readonly string[] Order =
    [
        RiskIndexSources.RegResidual, RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.SscTotal,
        RiskIndexSources.TenableNessus, RiskIndexSources.Importers, RiskIndexSources.Incidents,
        RiskIndexSources.NearMisses, RiskIndexSources.IncidentResponsePlans, RiskIndexSources.Assessments,
        RiskIndexSources.TenableVm, RiskIndexSources.TenableAes, RiskIndexSources.V1InternetFacing,
        RiskIndexSources.V1Accounts, RiskIndexSources.V1Cloud, RiskIndexSources.V1LocalApps,
        RiskIndexSources.V1SecurityConfiguration
    ];

    public static IReadOnlyList<string> Sources => Order;

    public static int Bit(string source)
    {
        var index = Array.IndexOf(Order, source);
        return index < 0 ? 0 : 1 << index;
    }

    public static int Mask(IEnumerable<string?> sources) => sources.Where(s => s is not null).Aggregate(0, (m, s) => m | Bit(s!));

    public static bool Has(int mask, string source) => (mask & Bit(source)) != 0;
}
