using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAL.Enums;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex;

// The JSON the snapshot job stores in the TEXT/LONGTEXT columns of risk_index_object_days and risk_index_snapshots (S39
// §9.1, §12.5; S40 §7.1, §7.7). One contract, here, for the writer and for every reader (the D−1 rebuild below, the
// 90-day replay of T250 and the read API of wave 4): snake_case names, enums as numbers, nulls omitted. The rows are
// history kept 400 and 1825 days, so a name is only ever added, never renamed.

/// <summary>One factor reading of an object row (<c>risk_index_object_days.factors</c>).</summary>
public sealed record StoredFactor(
    [property: JsonPropertyName("factor")] string Factor,
    [property: JsonPropertyName("type")] RiskIndexFactorType Type,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("v")] double V,
    [property: JsonPropertyName("u")] double U,
    [property: JsonPropertyName("read_at")] DateTime? ReadAt,
    [property: JsonPropertyName("source")] string? Source);

/// <summary>One normalized signal of an object row (<c>risk_index_object_days.signals</c>): only s &gt; 0, at most 50 per factor.</summary>
public sealed record StoredSignal(
    [property: JsonPropertyName("factor")] string Factor,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("s")] double Value,
    [property: JsonPropertyName("flags")] IReadOnlyList<string>? Flags);

/// <summary>An object, as the snapshot JSON names it.</summary>
public sealed record StoredObjectRef(
    [property: JsonPropertyName("kind")] RiskIndexObjectKind Kind,
    [property: JsonPropertyName("ref")] string Ref)
{
    public static StoredObjectRef Of(IcrObjectKey key) => new(key.Kind, key.Ref);
}

/// <summary>
/// One of the largest contributions of a node's headline (<c>risk_index_snapshots.top_contributions</c>, headline row only):
/// its points, and the indicator sensitivity when computed (S39 §4.8) — "what the index would be with the object cleared".
/// </summary>
public sealed record StoredContribution(
    [property: JsonPropertyName("object")] StoredObjectRef Object,
    [property: JsonPropertyName("category")] RiskIndexCategory Category,
    [property: JsonPropertyName("points")] double Points,
    [property: JsonPropertyName("presumed")] bool Presumed,
    [property: JsonPropertyName("holds_floor")] bool HoldsFloor,
    [property: JsonPropertyName("change_if_cleared")] double? ChangeIfCleared,
    [property: JsonPropertyName("next_holders")] IReadOnlyList<StoredObjectRef>? NextHolders);

/// <summary>One object's share of a node's change (<c>risk_index_snapshots.change_attribution.entries</c>).</summary>
public sealed record StoredAttributionEntry(
    [property: JsonPropertyName("object")] StoredObjectRef Object,
    [property: JsonPropertyName("category")] RiskIndexCategory Category,
    [property: JsonPropertyName("membership")] IcrMembership Membership,
    [property: JsonPropertyName("risk")] double Risk,
    [property: JsonPropertyName("context")] double Context,
    [property: JsonPropertyName("reattribution")] bool Reattribution);

/// <summary>
/// "What moved" for a node against the previous snapshot (S39 §9.4; <c>risk_index_snapshots.change_attribution</c>, headline
/// row only). The entries are the <see cref="RiskIndexStoredJson.MaxAttributionEntries"/> largest by magnitude; the rest
/// are summed in <see cref="OmittedTotal"/>, so the parts still reconcile with <see cref="Change"/>.
/// </summary>
public sealed record StoredChangeAttribution
{
    [JsonPropertyName("compared_with")] public DateOnly ComparedWith { get; init; }
    [JsonPropertyName("before")] public double? Before { get; init; }
    [JsonPropertyName("after")] public double? After { get; init; }
    [JsonPropertyName("change")] public double? Change { get; init; }
    [JsonPropertyName("configuration")] public double Configuration { get; init; }
    [JsonPropertyName("coverage")] public double Coverage { get; init; }
    [JsonPropertyName("restructuring")] public double Restructuring { get; init; }
    [JsonPropertyName("unattributed")] public double Unattributed { get; init; }
    /// <summary>Σ of the object shares per category, keyed by the category's number ("1" REG … "6" TER).</summary>
    [JsonPropertyName("by_category")] public IReadOnlyDictionary<string, double> ByCategory { get; init; } =
        new Dictionary<string, double>();
    [JsonPropertyName("entries")] public IReadOnlyList<StoredAttributionEntry> Entries { get; init; } = [];
    [JsonPropertyName("omitted_entries")] public int OmittedEntries { get; init; }
    [JsonPropertyName("omitted_total")] public double OmittedTotal { get; init; }
    [JsonPropertyName("steps")] public int Steps { get; init; }
    [JsonPropertyName("segments")] public int Segments { get; init; }
}

/// <summary>A discontinuity marker of a node on a day (S39 §9.5; <c>risk_index_snapshots.markers</c>), as the S40 §8.3 DTO.</summary>
public sealed record StoredMarker(
    [property: JsonPropertyName("kind")] RiskIndexMarkerKind Kind,
    [property: JsonPropertyName("args")] IReadOnlyDictionary<string, string> Args);

/// <summary>One day of a bridge's overlay (<c>risk_index_bridges.overlay</c>): the new profile replayed on a stored day.</summary>
public sealed record StoredOverlayPoint(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("value")] double Value);

/// <summary>A chip of a snapshot row (<c>risk_index_snapshots.chips</c>), as the S40 §8.3 <c>RiskIndexChipDto</c> key and args.</summary>
public sealed record StoredChip(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("args")] IReadOnlyDictionary<string, string>? Args = null);

/// <summary>The chip keys the job writes. Stable: wave 4 maps each to <c>RiskChip_‹key›MSG</c>.</summary>
public static class RiskIndexChipKeys
{
    /// <summary>Category row: no source of the category is enabled in the profile (A_cov reads it the next day).</summary>
    public const string SourceDisabled = "source_disabled";

    /// <summary>Category row: AME has no incident in <c>janela_intake_incidentes</c> — unavailable, not zero.</summary>
    public const string NoIncidentIntake = "no_incident_intake";

    /// <summary>Category row: only part of the category is linkable at this node (interim EXP of a process: "requires M39").</summary>
    public const string Partial = "partial";

    /// <summary>Category row: unavailable at this node by construction (interim EXP of an activity: "requires M39").</summary>
    public const string RequiresM39 = "requires_m39";

    /// <summary>Category row: the tail floor <c>100·τ·max R</c> sets the value; args <c>held_by</c>.</summary>
    public const string TailFloor = "tail_floor";

    /// <summary>Category row: presumed objects taken by the conservative inclusion; args <c>count</c>.</summary>
    public const string PresumedIncluded = "presumed_included";

    /// <summary>Headline row: <c>τ_G·max C_k</c> sets the headline; args <c>category</c>.</summary>
    public const string HeadlineFloor = "headline_floor";

    /// <summary>Headline row: no process of the node is declared critical, so <c>cov_processos</c> measured nothing.</summary>
    public const string NoCriticalProcess = "no_critical_process";

    /// <summary>Headline row: the node's closure reaches entities only through EAV links — a scoped read is partial (S39 §10.4).</summary>
    public const string ClosureThroughEav = "closure_through_eav";

    /// <summary>Root headline row: the Unassigned share of hosts and applications by weight above <c>limiar_nao_atribuido</c>.</summary>
    public const string UnassignedAboveThreshold = "unassigned_above_threshold";

    /// <summary>Root headline row: the data-quality counts of S39 §8.5 and §10.1; args per count.</summary>
    public const string DataQuality = "data_quality";
}

public static class RiskIndexStoredJson
{
    /// <summary>How many attribution entries a headline row keeps (S40 §7.7 volume); the rest are summed.</summary>
    public const int MaxAttributionEntries = 100;

    /// <summary>The signals kept per factor (<c>k_sinais_max</c>, S39 §9.1).</summary>
    public const int MaxSignalsPerFactor = 50;

    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) => string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>The factors of an object row: never null — "[]" for an assessed-clean object with none (S40 §7.7).</summary>
    public static string Factors(IEnumerable<RiskIndexFactorReading> factors) =>
        Serialize(factors.Select(f => new StoredFactor(f.Factor, f.Type, f.X, f.Validity, f.Ceiling, f.ReadAt, f.Source)).ToList());

    /// <summary>The signals with s &gt; 0, at most 50 per factor, largest first; null when none (S40 §7.7).</summary>
    public static string? Signals(IEnumerable<RiskIndexSignalReading> signals)
    {
        var kept = signals.Where(s => s.Value > 0)
            .GroupBy(s => s.Factor, StringComparer.Ordinal)
            .SelectMany(g => g.OrderByDescending(s => s.Value).Take(MaxSignalsPerFactor))
            .Select(s => new StoredSignal(s.Factor, s.Id, s.Value, s.Flags.Count == 0 ? null : s.Flags))
            .ToList();
        return kept.Count == 0 ? null : Serialize(kept);
    }

    public static string Attribution(IEnumerable<int> entities) => Serialize(entities.Distinct().OrderBy(e => e).ToList());

    public static IReadOnlyList<int> ParseAttribution(string? json) => Deserialize<List<int>>(json) ?? [];

    public static string? ScenarioInputs(RiskIndexScenarioInputs? inputs) => inputs is null ? null : Serialize(inputs);

    public static RiskIndexScenarioInputs? ParseScenarioInputs(string? json) => Deserialize<RiskIndexScenarioInputs>(json);

    /// <summary>The headline row's largest contributions, each with its sensitivity when the engine computed one.</summary>
    public static string? TopContributions(IcrNodeResult result)
    {
        if (result.TopContributions.Count == 0) return null;
        var sensitivity = result.Sensitivity.GroupBy(s => (s.Key, s.Category)).ToDictionary(g => g.Key, g => g.First());
        return Serialize(result.TopContributions.Select(c =>
        {
            sensitivity.TryGetValue((c.Key, c.Category), out var s);
            return new StoredContribution(StoredObjectRef.Of(c.Key), c.Category, c.Points, c.Presumed, c.HoldsFloor,
                s?.ChangeIfCleared, s is null || s.NextHolders.Count == 0 ? null : s.NextHolders.Select(StoredObjectRef.Of).ToList());
        }).ToList());
    }

    public static string? ChangeAttribution(IcrChangeAttributionResult? change, DateOnly comparedWith)
    {
        if (change is null) return null;
        var ordered = change.Entries.OrderByDescending(e => Math.Abs(e.Total)).ToList();
        var kept = ordered.Take(MaxAttributionEntries).ToList();
        var omitted = ordered.Skip(MaxAttributionEntries).ToList();
        return Serialize(new StoredChangeAttribution
        {
            ComparedWith = comparedWith, Before = change.Before, After = change.After, Change = change.Change,
            Configuration = change.ConfigurationEffect, Coverage = change.CoverageEffect,
            Restructuring = change.RestructuringEffect, Unattributed = change.Unattributed,
            ByCategory = change.ByCategory.ToDictionary(kv => Invariant((int)kv.Key), kv => kv.Value),
            Entries = kept.Select(e => new StoredAttributionEntry(StoredObjectRef.Of(e.Key), e.Category, e.Membership, e.RiskEffect,
                e.ContextEffect, e.Reattribution)).ToList(),
            OmittedEntries = omitted.Count, OmittedTotal = omitted.Sum(e => e.Total), Steps = change.Steps,
            Segments = change.Segments
        });
    }

    public static string? Markers(IReadOnlyCollection<StoredMarker> markers) =>
        markers.Count == 0 ? null : Serialize(markers.OrderBy(m => m.Kind).ToList());

    public static IReadOnlyList<StoredMarker> ParseMarkers(string? json) => Deserialize<List<StoredMarker>>(json) ?? [];

    public static string? Chips(IReadOnlyCollection<StoredChip> chips) => chips.Count == 0 ? null : Serialize(chips);

    public static IReadOnlyList<StoredChip> ParseChips(string? json) => Deserialize<List<StoredChip>>(json) ?? [];

    internal static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
