using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Model.RiskFlags;
using Tools.RiskFlags;
using Xunit;

namespace Tools.Tests.RiskFlags;

/// <summary>
/// Stage 9.5 (S46 §4.9, §8 TR1–TR7) — the trend column of the Top Risks list: 90-day window, ±0.5 on the
/// ordinal scale, baseline = the latest point at or before the window start.
/// </summary>
[TestSubject(typeof(RiskTrendCalculator))]
public class RiskTrendCalculatorTest
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static (DateTime, double) P(int daysAgo, double value) => (Now.AddDays(-daysAgo), value);

    /// <summary>TR1 — no history, or one point, is Unknown; one point still reports the current value.</summary>
    [Fact]
    public void TestTR1_FewerThanTwoPointsIsUnknown()
    {
        var none = RiskTrendCalculator.Compute([], Now);
        Assert.Equal(RiskTrendDirection.Unknown, none.Direction);
        Assert.Null(none.Current);
        Assert.Equal(0, none.Points);

        var one = RiskTrendCalculator.Compute([P(10, 6.0)], Now);
        Assert.Equal(RiskTrendDirection.Unknown, one.Direction);
        Assert.Equal(6.0, one.Current);
        Assert.Null(one.Delta);
    }

    /// <summary>TR2 — a rise of at least the threshold is Rising, exactly at the threshold included.</summary>
    [Fact]
    public void TestTR2_ARiseOfAtLeastTheThresholdIsRising()
    {
        var trend = RiskTrendCalculator.Compute([P(120, 5.0), P(1, 5.5)], Now);

        Assert.Equal(RiskTrendDirection.Rising, trend.Direction);
        Assert.Equal(0.5, trend.Delta);
        Assert.Equal(5.0, trend.Baseline);
        Assert.Equal(5.5, trend.Current);
    }

    /// <summary>TR3 — a fall of at least the threshold is Falling.</summary>
    [Fact]
    public void TestTR3_AFallIsFalling()
    {
        var trend = RiskTrendCalculator.Compute([P(100, 8.0), P(5, 6.0)], Now);

        Assert.Equal(RiskTrendDirection.Falling, trend.Direction);
        Assert.Equal(-2.0, trend.Delta);
    }

    /// <summary>TR4 — a move inside the threshold is Stable, not a trend.</summary>
    [Fact]
    public void TestTR4_AMoveInsideTheThresholdIsStable()
    {
        var trend = RiskTrendCalculator.Compute([P(100, 6.0), P(5, 6.4)], Now);

        Assert.Equal(RiskTrendDirection.Stable, trend.Direction);
        Assert.Equal(0.4, trend.Delta!.Value, 6);
    }

    /// <summary>
    /// TR5 — the baseline is the most recent point at or before the window start, not the oldest: a risk
    /// that peaked a year ago and has sat flat for the last quarter is Stable.
    /// </summary>
    [Fact]
    public void TestTR5_TheBaselineIsTheLatestPointBeforeTheWindow()
    {
        var trend = RiskTrendCalculator.Compute([P(365, 9.0), P(95, 4.0), P(30, 4.2), P(1, 4.1)], Now);

        Assert.Equal(RiskTrendDirection.Stable, trend.Direction);
        Assert.Equal(4.0, trend.Baseline);
        Assert.Equal(Now.AddDays(-95), trend.BaselineAt);
        Assert.Equal(4, trend.Points);
    }

    /// <summary>TR6 — a history shorter than the window compares against its oldest point.</summary>
    [Fact]
    public void TestTR6_AShortHistoryUsesItsOldestPoint()
    {
        var trend = RiskTrendCalculator.Compute([P(40, 3.0), P(20, 3.2), P(2, 7.0)], Now);

        Assert.Equal(RiskTrendDirection.Rising, trend.Direction);
        Assert.Equal(3.0, trend.Baseline);
        Assert.Equal(4.0, trend.Delta);
    }

    /// <summary>TR7 — the order of the input does not matter, and points dated in the future are ignored.</summary>
    [Fact]
    public void TestTR7_InputOrderIsIrrelevantAndFuturePointsAreIgnored()
    {
        List<(DateTime, double)> ordered = [P(100, 2.0), P(50, 3.0), P(1, 5.0)];
        var shuffled = new List<(DateTime, double)> { ordered[2], ordered[0], ordered[1], (Now.AddDays(3), 1.0) };

        var a = RiskTrendCalculator.Compute(ordered, Now);
        var b = RiskTrendCalculator.Compute(shuffled, Now);

        Assert.Equal(a.Direction, b.Direction);
        Assert.Equal(a.Delta, b.Delta);
        Assert.Equal(5.0, b.Current);
        Assert.Equal(3, b.Points);
    }

    /// <summary>Guard — a window or threshold that is not positive is a programming error.</summary>
    [Fact]
    public void TestNonPositiveParametersAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskTrendCalculator.Compute([], Now, windowDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskTrendCalculator.Compute([], Now, threshold: 0));
        Assert.Throws<ArgumentNullException>(() => RiskTrendCalculator.Compute(null!, Now));
        Assert.Equal(RiskTrendCalculator.DefaultWindowDays,
            RiskTrendCalculator.Compute(Enumerable.Empty<(DateTime, double)>(), Now).WindowDays);
    }
}
