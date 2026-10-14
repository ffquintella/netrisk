using System;
using JetBrains.Annotations;
using Tools.Risks;
using Xunit;

namespace Tools.Tests.Risks;

/// <summary>
/// T238 (S39 §4.5): <c>Faixa</c> has inclusive lower bounds and a reachable Very High. Every
/// boundary of the register limits (0, 4, 7, 9) is asserted exactly on, just below, and at the ends
/// of the scale.
/// </summary>
[TestSubject(typeof(RiskBand))]
public class RiskBandTest
{
    [Theory]
    [InlineData(0.0, RiskBand.Low)]          // min score: the old strict '>' left it without a band
    [InlineData(3.99, RiskBand.Low)]
    [InlineData(4.0, RiskBand.Medium)]       // exactly on the bound: inclusive
    [InlineData(6.99, RiskBand.Medium)]
    [InlineData(7.0, RiskBand.High)]
    [InlineData(8.99, RiskBand.High)]
    [InlineData(9.0, RiskBand.VeryHigh)]
    [InlineData(10.0, RiskBand.VeryHigh)]    // max score: Very High is reachable
    public void RegisterLimitsHaveInclusiveLowerBoundsAndAReachableVeryHigh(double score, int expected)
    {
        Assert.Equal(expected, RiskBand.Faixa(score, RiskBand.DefaultRegisterLimits));
    }

    [Fact]
    public void ADefaultVeryHighBoundIsReachableOnTheScale()
    {
        Assert.True(RiskBand.DefaultRegisterLimits[RiskBand.VeryHigh] <= 10.0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    [InlineData(31, 1)]
    [InlineData(69, 1)]
    [InlineData(70, 2)]
    [InlineData(100, 2)]
    public void IcrBandsClassifyTheDisplayedInteger(double icr, int expected)
    {
        Assert.Equal(expected, RiskBand.Faixa(icr, [0.0, 31.0, 70.0]));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void AScoreWithNoBandIsMinusOne(double score)
    {
        Assert.Equal(-1, RiskBand.Faixa(score, RiskBand.DefaultRegisterLimits));
    }

    [Fact]
    public void NoBoundsMeansNoBand()
    {
        Assert.Equal(-1, RiskBand.Faixa(5.0, []));
    }

    [Fact]
    public void AScoreAboveTheTopBoundStaysInTheTopBand()
    {
        Assert.Equal(RiskBand.VeryHigh, RiskBand.Faixa(1000.0, RiskBand.DefaultRegisterLimits));
        Assert.Equal(RiskBand.VeryHigh, RiskBand.Faixa(double.PositiveInfinity, RiskBand.DefaultRegisterLimits));
    }

    [Fact]
    public void ASeededUnreachableBoundNeverClassifiesAScoreOfTen()
    {
        // The seeded risk_levels row (10.1) is exactly what made Very High unreachable (D-02).
        Assert.Equal(RiskBand.High, RiskBand.Faixa(10.0, [0.0, 4.0, 7.0, 10.1]));
    }

    [Fact]
    public void NullBoundsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => RiskBand.Faixa(1.0, null!));
    }
}
