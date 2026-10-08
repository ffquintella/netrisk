using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.Monitoring;

namespace Tools.Monitoring;

/// <summary>
/// Gate B by indicator (Stage 9.8, S49 §4.8): the KRIs linked to a risk against their own tolerances.
///
/// One KRI beyond its tolerance — or stale with its last reading beyond it (S49 D4) — makes the gate exceed, and the
/// state of the others does not soften it. Otherwise a KRI with no reading or a stale one makes the gate
/// <see cref="IndicatorAppetiteState.NotAssessable"/> — never within tolerance. A retired KRI is ignored.
/// </summary>
public static class KriAppetite
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Gate B over the KRIs linked to one risk, each already evaluated (<see cref="KriGateDto.State"/>).</summary>
    public static IndicatorAppetiteEvaluation Evaluate(IEnumerable<KriGateDto> linked)
    {
        ArgumentNullException.ThrowIfNull(linked);

        var kris = linked.Where(k => k.State != KriState.Retired).OrderBy(k => k.KriId).ToList();
        var evaluation = new IndicatorAppetiteEvaluation { Kris = kris };

        if (kris.Count == 0)
        {
            evaluation.State = IndicatorAppetiteState.NotConfigured;
            evaluation.Explanation = "No key risk indicator is linked to this risk, so no indicator gates it.";
            return evaluation;
        }

        var exceeding = kris.Where(k => k.Exceeds).ToList();
        if (exceeding.Count > 0)
        {
            evaluation.State = IndicatorAppetiteState.ExceedsTolerance;
            evaluation.Explanation =
                $"{Describe(exceeding)} {(exceeding.Count == 1 ? "is" : "are")} beyond tolerance. Treat or escalate " +
                "(Gate B) — this risk cannot be accepted as it stands.";
            return evaluation;
        }

        if (kris.Any(k => k.State == KriState.NoReading)) evaluation.Reasons.Add(IndicatorNotAssessableReason.NoReading);
        if (kris.Any(k => k.State == KriState.Stale)) evaluation.Reasons.Add(IndicatorNotAssessableReason.Stale);

        if (evaluation.Reasons.Count > 0)
        {
            var dark = kris.Where(k => k.State is KriState.NoReading or KriState.Stale).ToList();
            evaluation.State = IndicatorAppetiteState.NotAssessable;
            evaluation.Explanation =
                $"{Describe(dark)} {(dark.Count == 1 ? "has" : "have")} no current reading, so Gate B by indicator " +
                "cannot be assessed; it is not read as within tolerance.";
            return evaluation;
        }

        evaluation.State = IndicatorAppetiteState.WithinTolerance;
        evaluation.Explanation = kris.Count == 1
            ? "The linked indicator is within its tolerance."
            : $"All {kris.Count} linked indicators are within their tolerance.";
        return evaluation;
    }

    /// <summary>Whether a KRI in <paramref name="status"/> makes the gate exceed (S49 D4).</summary>
    public static bool Exceeds(KriStatusDto status) => status.State switch
    {
        KriState.Breached => true,
        KriState.Stale => status.LastReadingBreached,
        _ => false
    };

    /// <summary>One KRI as the gate reads it, from its definition and its evaluated status.</summary>
    public static KriGateDto Gate(int kriId, string name, DAL.Enums.KriCategory category, string unit,
        DAL.Enums.KriDirection direction, decimal tolerance, KriStatusDto status) => new()
    {
        KriId = kriId,
        Name = name,
        Category = category,
        Unit = unit,
        Direction = direction,
        ToleranceThreshold = tolerance,
        State = status.State,
        Value = status.LatestValue,
        ObservedAt = status.LatestObservedAt,
        Exceeds = Exceeds(status)
    };

    private static string Describe(IReadOnlyList<KriGateDto> kris) => string.Join("; ", kris.Select(k =>
        k.Value is { } value
            ? $"'{k.Name}' ({value.ToString("0.####", Invariant)} {k.Unit}, tolerance {k.ToleranceThreshold.ToString("0.####", Invariant)})"
            : $"'{k.Name}'"));
}
