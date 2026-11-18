using System;
using Tools.Math;
using Xunit;

namespace Tools.Tests.Math;

/// <summary>S56: the .NET 11 <c>(decimal)double</c> is exact, which broke short decimals; this keeps the old result.</summary>
public class DecimalConversionTest
{
    [Theory]
    [InlineData(0.01, "0.01")]
    [InlineData(0.5, "0.5")]
    [InlineData(1.0, "1")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.5, "1.5")]
    [InlineData(-0.25, "-0.25")]
    [InlineData(0.0, "0")]
    [InlineData(1e-5, "0.00001")]
    public void TestAShortDecimalSurvivesTheRoundTrip(double value, string expected)
    {
        var converted = DecimalConversion.FromDouble(value);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), converted);
    }

    [Fact]
    public void TestTheConvertedValueHasAtMostTwoDecimalsWhenTheSourceDid()
    {
        var converted = DecimalConversion.FromDouble(0.01);

        Assert.Equal(decimal.Round(converted, 2), converted);
    }

    [Fact]
    public void TestAHourTotalFromATimeSpanIsNotPaddedWithNoise()
    {
        var hours = TimeSpan.FromMinutes(90).TotalHours;

        Assert.Equal(1.5m, DecimalConversion.FromDouble(hours));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TestANonFiniteValueThrowsLikeTheCastItReplaces(double value)
    {
        Assert.Throws<OverflowException>(() => DecimalConversion.FromDouble(value));
    }
}
