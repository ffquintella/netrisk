using System;
using System.Linq;
using JetBrains.Annotations;
using ServerServices.RiskIndex.Engine;
using Xunit;
using static ServerServices.Tests.RiskIndex.IcrFixtures;

namespace ServerServices.Tests.RiskIndex;

/// <summary>The scalar primitives and normalization anchors of the ICR (S39 §3.1, §4.2, §4.5, §5.1, §6.5, §8.2).</summary>
[TestSubject(typeof(IcrAnchors))]
public class IcrPrimitivesTest
{
    [Theory]
    [InlineData(1, 0.40)]
    [InlineData(2, 0.55)]
    [InlineData(3, 0.70)]
    [InlineData(4, 0.85)]
    [InlineData(5, 1.00)]
    [InlineData(0, 0.40)]
    [InlineData(9, 1.00)]
    [InlineData(-3, 0.40)]
    public void TestTheModulatorClampsCriticalityToTheScale(int crit, double expected) =>
        Assert.Equal(expected, IcrMath.Modulator(crit, 0.40), 10);

    [Fact]
    public void TestFiftyMediumFindingsStayBelowOneExploitedCritical()
    {
        var fifty = IcrMath.Mra(Enumerable.Repeat(0.5, 50), 0.30, 10);

        Assert.Equal(0.5 + 0.3 * 0.5 * (1 - Math.Pow(0.5, 9)), fifty, 12);
        Assert.True(fifty < 0.95);
    }

    [Fact]
    public void TestMraIsBoundedByOneAndAtLeastTheMaximum()
    {
        double[] values = [0.3, 0.9, 0.7, 0.2];
        var mra = IcrMath.Mra(values, 0.3, 10);

        Assert.InRange(mra, 0.9, 0.9 + 0.3 * 0.1);
        Assert.Equal(1.0, IcrMath.Mra([1.0, 0.5], 0.3, 10), 12);
        Assert.Equal(0, IcrMath.Mra([], 0.3, 10));
    }

    [Fact]
    public void TestMraSharesSumExactlyToTheValueAndTheLeaderGetsItsOwnValue()
    {
        double[] values = [0.3, 0.9, 0.7, 0.2, 0];
        var shares = IcrMath.MraShares(values, 0.3, 10);

        Assert.Equal(IcrMath.Mra(values, 0.3, 10), shares.Sum(), 12);
        Assert.Equal(0.9, shares[1], 12);
        Assert.Equal(0, shares[4]);
        Assert.True(shares[2] > shares[0] && shares[0] > shares[3]);
    }

    [Fact]
    public void TestMraKeepsOnlyTheKLargest()
    {
        var values = Enumerable.Repeat(0.4, 30).ToArray();

        Assert.Equal(IcrMath.Mra(values.Take(3), 0.3, 3), IcrMath.Mra(values, 0.3, 3), 12);
        Assert.Equal(27, IcrMath.MraShares(values, 0.3, 3).Count(s => s == 0));
    }

    [Theory]
    [InlineData(5, 7, 1.0)]
    [InlineData(7, 7, 1.0)]
    [InlineData(10.5, 7, 0.5)]
    [InlineData(14, 7, 0.0)]
    [InlineData(45, 30, 0.5)]
    [InlineData(70, 30, 0.0)]
    public void TestReadingValidity(double age, double window, double expected) =>
        Assert.Equal(expected, IcrMath.Validity(age, window), 10);

    [Fact]
    public void TestAStaleBadReadingKeepsItsValueAndAStaleGoodOneSlidesUp()
    {
        // S39 §8.2: crit-4 host, Vision One 90 read 15 days ago (W = 7, v = 0): EXP.D enters at 0.90.
        Assert.Equal(0.90, IcrMath.EffectiveFactor(0.90, 0, 0.5, M(4)), 12);
        Assert.Equal(0.425, IcrMath.EffectiveFactor(0, 0, 0.5, M(4)), 12);
        Assert.Equal(0.2125, IcrMath.EffectiveFactor(0, 0.5, 0.5, M(4)), 12);
    }

    [Theory]
    [InlineData(63.5, 64)]
    [InlineData(62.5, 63)]
    [InlineData(69.49, 69)]
    [InlineData(69.5, 70)]
    [InlineData(0.5, 1)]
    public void TestDisplayRoundsHalfAwayFromZero(double value, int expected) =>
        Assert.Equal(expected, IcrMath.Displayed(value));

    [Theory]
    [InlineData(100, 0.00)]
    [InlineData(90, 0.10)]
    [InlineData(72, 0.51)]
    [InlineData(59, 0.765)]
    [InlineData(50, 0.90)]
    [InlineData(0, 1.00)]
    [InlineData(120, 0.00)]
    public void TestSecurityScorecardAnchors(double score, double expected) =>
        Assert.Equal(expected, IcrAnchors.Ssc(Balanced, score), 10);

    [Fact]
    public void TestExploitationFloorsTheSignalWhateverTheBase()
    {
        var vpr = IcrAnchors.Finding(Balanced, new FindingReading { Vpr = 3.0, ExploitationKnown = true })!.Value;
        Assert.Equal(0.95, vpr.Value, 12);
        Assert.True(vpr.ExploitationFloor);

        var internet = IcrAnchors.Finding(Balanced, new FindingReading { Cvss3 = 9.8, ExploitationKnown = true, InternetFacing = true })!.Value;
        Assert.Equal(1.0, internet.Value, 12);
    }

    [Fact]
    public void TestEveryExploitedFindingRanksAboveEveryUnexploitedOne()
    {
        var worstUnexploited = IcrAnchors.Finding(Balanced, new FindingReading
        {
            Cvss3 = 10, ExploitAvailable = true, Epss = 0.9, InternetFacing = true
        })!.Value.Value;
        var leastExploited = IcrAnchors.Finding(Balanced, new FindingReading { Severity = 1, ExploitationKnown = true })!.Value.Value;

        Assert.Equal(0.90, worstUnexploited, 12);
        Assert.True(leastExploited > worstUnexploited);
    }

    [Fact]
    public void TestTheExploitMultiplierAppliesToCvssAndSeverityButNotToVpr()
    {
        Assert.Equal(0.67, IcrAnchors.Finding(Balanced, new FindingReading { Vpr = 6.7, ExploitAvailable = true })!.Value.Value, 12);
        Assert.Equal(0.6095, IcrAnchors.Finding(Balanced, new FindingReading { Cvss3 = 5.3, ExploitAvailable = true })!.Value.Value, 12);
        Assert.Equal(0.6095, IcrAnchors.Finding(Balanced, new FindingReading { Cvss3 = 5.3, Epss = 0.10 })!.Value.Value, 12);
        Assert.Equal(0.53, IcrAnchors.Finding(Balanced, new FindingReading { Cvss3 = 5.3, Epss = 0.09 })!.Value.Value, 12);
        Assert.Equal(0.5 * 1.15, IcrAnchors.Finding(Balanced, new FindingReading { Severity = 2, ExploitAvailable = true })!.Value.Value, 12);
    }

    [Fact]
    public void TestAFindingNothingScoresIsExcluded() =>
        Assert.Null(IcrAnchors.Finding(Balanced, new FindingReading { Severity = 0 }));

    [Fact]
    public void TestIncidentAnchorsAndDecay()
    {
        Assert.Equal(0.90, IcrAnchors.Incident(Balanced, "data_breach", null));
        Assert.Equal(0.50, IcrAnchors.Incident(Balanced, "something_new", null));
        Assert.Equal(0.50, IcrAnchors.Incident(Balanced, null, null));
        Assert.Equal(1.00, IcrAnchors.Incident(Balanced, "phishing", "Critical"));

        var now = new DateTime(2026, 10, 5);
        Assert.Equal(1.0, IcrAnchors.IncidentDecay(Balanced, true, null, now));
        Assert.Equal(0.125, IcrAnchors.IncidentDecay(Balanced, false, now.AddDays(-90), now)!.Value, 12);
        Assert.Null(IcrAnchors.IncidentDecay(Balanced, false, now.AddDays(-91), now));
        Assert.Equal(0.315, 0.5 * IcrAnchors.IncidentDecay(Balanced, false, now.AddDays(-20), now)!.Value, 3);
    }

    [Theory]
    [InlineData(0.60, 0.0)]
    [InlineData(0.70, 0.5)]
    [InlineData(0.80, 1.0)]
    [InlineData(0.84, 1.0)]
    [InlineData(0.30, 0.0)]
    public void TestTheResponsePlanRamp(double sigma, double expected) =>
        Assert.Equal(expected, IcrAnchors.IrpRamp(Balanced, sigma), 10);

    [Fact]
    public void TestResponsePlanSignal()
    {
        var now = new DateTime(2026, 10, 5);
        Assert.Equal(1.0, IcrAnchors.Irp(Balanced, false, now, now));
        Assert.Equal(0.0, IcrAnchors.Irp(Balanced, true, now.AddDays(-365), now));
        Assert.Equal(0.5, IcrAnchors.Irp(Balanced, true, now.AddDays(-400), now));
        Assert.Equal(0.5, IcrAnchors.Irp(Balanced, true, null, now));
    }

    [Theory]
    [InlineData(1, 6, 1.30)]
    [InlineData(2, 6, 1.24)]
    [InlineData(6, 6, 1.00)]
    [InlineData(1, 1, 1.30)]
    [InlineData(null, 6, 1.15)]
    [InlineData(9, 6, 1.00)]
    public void TestCampaignRankMultiplier(int? rank, int n, double expected) =>
        Assert.Equal(expected, IcrAnchors.RankMultiplier(Balanced, rank, n), 10);
}
