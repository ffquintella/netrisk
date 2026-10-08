using System;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Monitoring;
using Tools.Monitoring;
using Xunit;

namespace Tools.Tests.Monitoring;

/// <summary>
/// Stage 9.8 (S49 §4.8, D4, §8 KA1–KA7) — Gate B by indicator over the KRIs linked to a risk: one exceeding KRI makes
/// the gate exceed whatever the others say; otherwise a KRI with no current reading makes it not assessable, never
/// within tolerance.
/// </summary>
[TestSubject(typeof(KriAppetite))]
public class KriAppetiteTest
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static KriGateDto Kri(int id, KriState state, bool lastReadingBreached = false, decimal? value = 5m) =>
        KriAppetite.Gate(id, $"KRI {id}", KriCategory.Unavailability, "hours", KriDirection.HigherIsWorse, 8m,
            new KriStatusDto
            {
                State = state, LastReadingBreached = lastReadingBreached, LatestValue = value,
                LatestObservedAt = value is null ? null : Now.AddDays(-1), EvaluatedAt = Now
            });

    /// <summary>KA1 — nothing linked: the gate is not configured.</summary>
    [Fact]
    public void TestKA1_NoKriIsNotConfigured()
    {
        var gate = KriAppetite.Evaluate([]);

        Assert.Equal(IndicatorAppetiteState.NotConfigured, gate.State);
        Assert.Empty(gate.Kris);
    }

    /// <summary>KA2 — every KRI current and within (or in warning): within.</summary>
    [Fact]
    public void TestKA2_AllWithinIsWithin()
    {
        var gate = KriAppetite.Evaluate([Kri(1, KriState.WithinTolerance), Kri(2, KriState.Warning)]);

        Assert.Equal(IndicatorAppetiteState.WithinTolerance, gate.State);
        Assert.Empty(gate.Reasons);
        Assert.All(gate.Kris, k => Assert.False(k.Exceeds));
    }

    /// <summary>KA3 — one breached KRI exceeds the gate, and a stale neighbour does not soften it.</summary>
    [Fact]
    public void TestKA3_OneBreachExceedsWhateverTheOthersSay()
    {
        var gate = KriAppetite.Evaluate([Kri(1, KriState.Stale), Kri(2, KriState.Breached, true, 12m)]);

        Assert.Equal(IndicatorAppetiteState.ExceedsTolerance, gate.State);
        Assert.True(gate.Kris.Single(k => k.KriId == 2).Exceeds);
        Assert.Contains("'KRI 2' (12 hours, tolerance 8)", gate.Explanation);
    }

    /// <summary>KA4 (T193) — a stale KRI whose last reading was within is not assessable, never within.</summary>
    [Fact]
    public void TestKA4_AStaleKriIsNotAssessableNeverWithin()
    {
        var gate = KriAppetite.Evaluate([Kri(1, KriState.WithinTolerance), Kri(2, KriState.Stale)]);

        Assert.Equal(IndicatorAppetiteState.NotAssessable, gate.State);
        Assert.Equal([IndicatorNotAssessableReason.Stale], gate.Reasons);
        Assert.Contains("not read as within tolerance", gate.Explanation);
    }

    /// <summary>KA5 — stale, but the last reading was beyond the tolerance: nothing shows it recovered — exceeds (S49 D4).</summary>
    [Fact]
    public void TestKA5_AStaleBreachExceeds()
    {
        var gate = KriAppetite.Evaluate([Kri(1, KriState.Stale, lastReadingBreached: true, value: 20m)]);

        Assert.Equal(IndicatorAppetiteState.ExceedsTolerance, gate.State);
        Assert.True(Assert.Single(gate.Kris).Exceeds);
    }

    /// <summary>KA6 — a KRI never read: not assessable, with its own reason.</summary>
    [Fact]
    public void TestKA6_NoReadingIsNotAssessable()
    {
        var gate = KriAppetite.Evaluate([Kri(1, KriState.NoReading, value: null), Kri(2, KriState.Stale)]);

        Assert.Equal(IndicatorAppetiteState.NotAssessable, gate.State);
        Assert.Equal([IndicatorNotAssessableReason.NoReading, IndicatorNotAssessableReason.Stale], gate.Reasons);
    }

    /// <summary>KA7 — a retired KRI is ignored: alone, the gate is not configured; beside a breach, it changes nothing.</summary>
    [Fact]
    public void TestKA7_ARetiredKriIsIgnored()
    {
        Assert.Equal(IndicatorAppetiteState.NotConfigured, KriAppetite.Evaluate([Kri(1, KriState.Retired)]).State);

        var gate = KriAppetite.Evaluate([Kri(1, KriState.Retired), Kri(2, KriState.WithinTolerance)]);
        Assert.Equal(IndicatorAppetiteState.WithinTolerance, gate.State);
        Assert.Equal(2, Assert.Single(gate.Kris).KriId);
    }

    /// <summary>Only breached, or stale with a breached last reading, exceeds.</summary>
    [Theory]
    [InlineData(KriState.Breached, false, true)]
    [InlineData(KriState.Stale, true, true)]
    [InlineData(KriState.Stale, false, false)]
    [InlineData(KriState.Warning, false, false)]
    [InlineData(KriState.NoReading, false, false)]
    [InlineData(KriState.Retired, true, false)]
    public void TestWhichStatesExceed(KriState state, bool lastBreached, bool exceeds) =>
        Assert.Equal(exceeds, KriAppetite.Exceeds(new KriStatusDto { State = state, LastReadingBreached = lastBreached }));
}
