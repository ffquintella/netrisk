using System;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Monitoring;
using Tools.Monitoring;
using Xunit;

namespace Tools.Tests.Monitoring;

/// <summary>
/// Stage 9.8 (S49 §4.4, §8 KE1–KE10) — the state of a key risk indicator from its latest valid reading. The case the
/// track names first: a KRI with no recent reading reads <see cref="KriState.Stale"/>, never within tolerance (KE2).
/// </summary>
[TestSubject(typeof(KriEvaluator))]
public class KriEvaluatorTest
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Availability-style hours down: worse when higher, tolerance 8, warning 6, readings current for 31 days.</summary>
    private static readonly KriThresholds HoursDown = new(KriDirection.HigherIsWorse, 8m, 6m, 31);

    /// <summary>Backup success rate: worse when lower, tolerance 95, warning 98.</summary>
    private static readonly KriThresholds BackupSuccess = new(KriDirection.LowerIsWorse, 95m, 98m, 31);

    private static KriObservation At(int id, decimal value, double daysAgo, bool voided = false) =>
        new(id, value, Now.AddDays(-daysAgo), voided);

    /// <summary>KE1 — no reading at all: not assessable, never within.</summary>
    [Fact]
    public void TestKE1_NoReadingIsNoReading()
    {
        var status = KriEvaluator.Evaluate(HoursDown, [], Now);

        Assert.Equal(KriState.NoReading, status.State);
        Assert.Null(status.LatestValue);
        Assert.False(status.LastReadingBreached);
    }

    /// <summary>
    /// KE2 (T193) — a reading within tolerance but older than the maximum age is stale, not within tolerance: the false
    /// comfort an indicator panel must not give. Moving the staleness check after the value check fails this test.
    /// </summary>
    [Fact]
    public void TestKE2_AnOldReadingWithinToleranceIsStaleNotWithin()
    {
        var status = KriEvaluator.Evaluate(HoursDown, [At(1, 2m, 40)], Now);

        Assert.Equal(KriState.Stale, status.State);
        Assert.NotEqual(KriState.WithinTolerance, status.State);
        Assert.False(status.LastReadingBreached);
        Assert.Equal(40, status.AgeDays);
        Assert.Contains("stale", status.Explanation);
    }

    /// <summary>KE3 — exactly at the maximum age the reading still counts; one minute past it does not.</summary>
    [Fact]
    public void TestKE3_TheMaximumAgeIsInclusive()
    {
        Assert.Equal(KriState.WithinTolerance, KriEvaluator.Evaluate(HoursDown, [At(1, 2m, 31)], Now).State);
        Assert.Equal(KriState.Stale, KriEvaluator.Evaluate(HoursDown,
            [new KriObservation(1, 2m, Now.AddDays(-31).AddMinutes(-1))], Now).State);
    }

    /// <summary>KE4 — worse when higher: equal to the tolerance is not a breach (strict, S49 D3); above it is.</summary>
    [Theory]
    [InlineData(8, KriState.Warning)]
    [InlineData(8.0001, KriState.Breached)]
    [InlineData(12, KriState.Breached)]
    public void TestKE4_HigherIsWorseComparesStrictly(double value, KriState expected)
    {
        var status = KriEvaluator.Evaluate(HoursDown, [At(1, (decimal)value, 1)], Now);

        Assert.Equal(expected, status.State);
        Assert.Equal(expected == KriState.Breached, status.LastReadingBreached);
    }

    /// <summary>KE5 — worse when lower, mirrored.</summary>
    [Theory]
    [InlineData(99.5, KriState.WithinTolerance)]
    [InlineData(97, KriState.Warning)]
    [InlineData(95, KriState.Warning)]
    [InlineData(94.9, KriState.Breached)]
    public void TestKE5_LowerIsWorseIsMirrored(double value, KriState expected) =>
        Assert.Equal(expected, KriEvaluator.Evaluate(BackupSuccess, [At(1, (decimal)value, 1)], Now).State);

    /// <summary>KE6 — the warning is strict too, and a KRI with no warning goes straight from within to breached.</summary>
    [Fact]
    public void TestKE6_TheWarningThreshold()
    {
        Assert.Equal(KriState.WithinTolerance, KriEvaluator.Evaluate(HoursDown, [At(1, 6m, 1)], Now).State);
        Assert.Equal(KriState.Warning, KriEvaluator.Evaluate(HoursDown, [At(1, 6.5m, 1)], Now).State);

        var noWarning = HoursDown with { WarningThreshold = null };
        Assert.Equal(KriState.WithinTolerance, KriEvaluator.Evaluate(noWarning, [At(1, 7.9m, 1)], Now).State);
    }

    /// <summary>
    /// KE7 — the latest valid reading decides: a voided one is ignored, the latest observation wins over the latest
    /// typed, a tie on the observation goes to the higher id, and a reading observed after "now" is not read.
    /// </summary>
    [Fact]
    public void TestKE7_TheLatestValidReadingDecides()
    {
        // The voided breach is ignored.
        Assert.Equal(KriState.WithinTolerance,
            KriEvaluator.Evaluate(HoursDown, [At(1, 2m, 5), At(2, 20m, 1, voided: true)], Now).State);

        // Recorded later (higher id) but observed earlier: the later observation wins.
        Assert.Equal(KriState.Breached, KriEvaluator.Evaluate(HoursDown, [At(9, 2m, 5), At(3, 20m, 1)], Now).State);

        // Same observation instant: the higher id wins.
        var tie = Now.AddDays(-1);
        Assert.Equal(KriState.WithinTolerance, KriEvaluator.Evaluate(HoursDown,
            [new KriObservation(4, 20m, tie), new KriObservation(5, 2m, tie)], Now).State);

        // Observed after now: ignored, so the older reading is the latest.
        var status = KriEvaluator.Evaluate(HoursDown, [At(6, 2m, 3), new KriObservation(7, 20m, Now.AddHours(1))], Now);
        Assert.Equal((KriState.WithinTolerance, 6), (status.State, status.LatestReadingId!.Value));
    }

    /// <summary>KE8 — a retired KRI is retired whatever its readings.</summary>
    [Fact]
    public void TestKE8_ARetiredKriIsRetired() =>
        Assert.Equal(KriState.Retired,
            KriEvaluator.Evaluate(HoursDown with { Retired = true }, [At(1, 20m, 1)], Now).State);

    /// <summary>KE9 — stale with the last reading beyond the tolerance: stale, and the breach is carried (S49 D4).</summary>
    [Fact]
    public void TestKE9_AStaleBreachCarriesTheBreach()
    {
        var status = KriEvaluator.Evaluate(HoursDown, [At(1, 20m, 60)], Now);

        Assert.Equal(KriState.Stale, status.State);
        Assert.True(status.LastReadingBreached);
        Assert.Contains("nothing since shows it recovered", status.Explanation);
    }

    /// <summary>
    /// KE10 — the episode opens at the oldest reading of the unbroken run of breaches that ends with the latest one; a
    /// voided reading does not break the run; no breach now, no opening.
    /// </summary>
    [Fact]
    public void TestKE10_TheEpisodeOpeningIsTheStartOfTheCurrentRun()
    {
        var readings = new[]
        {
            At(1, 12m, 10), // an earlier episode
            At(2, 3m, 8), // recovery
            At(3, 9m, 6), // the current run opens here
            At(4, 2m, 5, voided: true), // voided — does not break the run
            At(5, 11m, 3),
            At(6, 15m, 1)
        };

        Assert.Equal(3, KriEvaluator.EpisodeOpening(HoursDown, readings, Now)!.Id);
        Assert.Null(KriEvaluator.EpisodeOpening(HoursDown, [At(1, 12m, 3), At(2, 3m, 1)], Now));
        Assert.Null(KriEvaluator.EpisodeOpening(HoursDown, [], Now));
    }

    [Fact]
    public void TestAnUndefinedDirectionIsAnArgumentError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => KriEvaluator.Breaches((KriDirection)9, 1m, 2m));
}
