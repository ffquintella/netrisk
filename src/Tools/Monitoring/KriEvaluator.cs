using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.Monitoring;

namespace Tools.Monitoring;

/// <summary>What the evaluation needs of a KRI's definition (S49 §4.1).</summary>
public sealed record KriThresholds(
    KriDirection Direction,
    decimal ToleranceThreshold,
    decimal? WarningThreshold,
    int MaxReadingAgeDays,
    bool Retired = false);

/// <summary>
/// One reading as the evaluation sees it (S49 §4.2). <paramref name="RecordedAt"/> is when it was written — what tells a
/// breach run that existed when an episode ended from one recorded after it (S49 §4.7, step 2).
/// </summary>
public sealed record KriObservation(int Id, decimal Value, DateTime ObservedAt, bool Voided = false,
    DateTime RecordedAt = default);

/// <summary>
/// The state of a key risk indicator (Stage 9.8, S49 §4.4) — pure, so the service, the job and Gate B agree.
///
/// Staleness is checked before the value: a KRI whose latest reading, months old, was within tolerance reads
/// <see cref="KriState.Stale"/>, never <see cref="KriState.WithinTolerance"/> (S49 D2, T193). The comparison with the
/// tolerance is strict (S49 D3).
/// </summary>
public static class KriEvaluator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Whether <paramref name="value"/> is strictly beyond <paramref name="tolerance"/> in the direction that is worse.</summary>
    public static bool Breaches(KriDirection direction, decimal tolerance, decimal value) => direction switch
    {
        KriDirection.HigherIsWorse => value > tolerance,
        KriDirection.LowerIsWorse => value < tolerance,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Undefined KRI direction.")
    };

    /// <summary>Whether <paramref name="value"/> is past the warning threshold (strictly), on the bad side of it.</summary>
    public static bool Warns(KriDirection direction, decimal? warning, decimal value) =>
        warning is { } w && direction switch
        {
            KriDirection.HigherIsWorse => value > w,
            KriDirection.LowerIsWorse => value < w,
            _ => false
        };

    /// <summary>
    /// The valid readings that count at <paramref name="now"/>, newest first: not voided, not observed after
    /// <paramref name="now"/> (the evaluation never reads the future), ordered by observation and then by id.
    /// </summary>
    public static List<KriObservation> Valid(IEnumerable<KriObservation> readings, DateTime now) =>
        readings.Where(r => !r.Voided && r.ObservedAt <= now)
            .OrderByDescending(r => r.ObservedAt)
            .ThenByDescending(r => r.Id)
            .ToList();

    /// <summary>The state of a KRI at <paramref name="now"/> from its readings (voided ones are ignored).</summary>
    public static KriStatusDto Evaluate(KriThresholds kri, IEnumerable<KriObservation> readings, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(kri);
        ArgumentNullException.ThrowIfNull(readings);

        var status = new KriStatusDto { EvaluatedAt = now };

        if (kri.Retired)
        {
            status.State = KriState.Retired;
            status.Explanation = "The indicator is retired: it is not evaluated and gates nothing.";
            return status;
        }

        var latest = Valid(readings, now).FirstOrDefault();
        if (latest is null)
        {
            status.State = KriState.NoReading;
            status.Explanation = "The indicator has no valid reading, so it cannot be read as within tolerance.";
            return status;
        }

        status.LatestReadingId = latest.Id;
        status.LatestValue = latest.Value;
        status.LatestObservedAt = latest.ObservedAt;
        status.AgeDays = (int)System.Math.Floor((now - latest.ObservedAt).TotalDays);
        status.LastReadingBreached = Breaches(kri.Direction, kri.ToleranceThreshold, latest.Value);

        // Staleness first, whatever the value: a reading older than the indicator's maximum age says nothing about
        // today, and reading it as "within" is the false comfort Stage 9.8 exists to remove.
        if (now - latest.ObservedAt > TimeSpan.FromDays(kri.MaxReadingAgeDays))
        {
            status.State = KriState.Stale;
            status.Explanation =
                $"The latest reading is {status.AgeDays} day(s) old, beyond the {kri.MaxReadingAgeDays}-day maximum: the " +
                "indicator is stale and is not read as within tolerance." +
                (status.LastReadingBreached
                    ? " That reading was beyond the tolerance, and nothing since shows it recovered."
                    : string.Empty);
            return status;
        }

        if (status.LastReadingBreached)
        {
            status.State = KriState.Breached;
            status.Explanation =
                $"{Format(latest.Value)} is beyond the tolerance of {Format(kri.ToleranceThreshold)} " +
                $"({(kri.Direction == KriDirection.HigherIsWorse ? "worse when higher" : "worse when lower")}).";
            return status;
        }

        if (Warns(kri.Direction, kri.WarningThreshold, latest.Value))
        {
            status.State = KriState.Warning;
            status.Explanation =
                $"{Format(latest.Value)} is past the warning of {Format(kri.WarningThreshold!.Value)} and within the " +
                $"tolerance of {Format(kri.ToleranceThreshold)}.";
            return status;
        }

        status.State = KriState.WithinTolerance;
        status.Explanation = $"{Format(latest.Value)} is within the tolerance of {Format(kri.ToleranceThreshold)}.";
        return status;
    }

    /// <summary>
    /// The reading that opened the breach in progress: the oldest of the unbroken run of valid readings beyond the
    /// tolerance that ends with the latest one (S49 §4.7). Null when the latest valid reading is not beyond it.
    /// </summary>
    public static KriObservation? EpisodeOpening(KriThresholds kri, IEnumerable<KriObservation> readings, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(kri);

        KriObservation? opening = null;
        foreach (var reading in Valid(readings, now))
        {
            if (!Breaches(kri.Direction, kri.ToleranceThreshold, reading.Value)) break;
            opening = reading;
        }

        return opening;
    }

    private static string Format(decimal value) => value.ToString("0.####", Invariant);
}
