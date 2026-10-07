using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Tools.Continuity;
using Xunit;

namespace Tools.Tests.Continuity;

/// <summary>
/// Stage 9.3 (S43 §8, CS1–CS6) — the two continuity parameters: the restoration-test validity
/// (default 365, 1–1 095) and the unverified-threat weight (default 0.5, 0.01–1.00, two places).
/// A damaged row reads as the default and is reported; it never becomes 0 days nor weight 0.
/// </summary>
[TestSubject(typeof(ContinuitySettings))]
public class ContinuitySettingsTest
{
    private const string Days = ContinuitySettingKeys.RestorationTestValidityDays;
    private const string Weight = ContinuitySettingKeys.UnverifiedThreatWeight;

    private static Dictionary<string, string?> Rows(string? days, string? weight) => new()
    {
        [Days] = days,
        [Weight] = weight
    };

    /// <summary>CS1 — missing rows read as the defaults, both reported; valid rows are used, none reported.</summary>
    [Fact]
    public void TestCS1_MissingRowsReadAsTheDefaultsAndAreReported()
    {
        var missing = ContinuitySettings.Parse(new Dictionary<string, string?>());
        Assert.Equal(365, missing.RestorationTestValidityDays);
        Assert.Equal(0.5m, missing.UnverifiedThreatWeight);
        Assert.Equal([Days, Weight], missing.FallbackApplied);

        var valid = ContinuitySettings.Parse(Rows("180", "0.25"));
        Assert.Equal(180, valid.RestorationTestValidityDays);
        Assert.Equal(0.25m, valid.UnverifiedThreatWeight);
        Assert.Empty(valid.FallbackApplied);
    }

    /// <summary>CS2 — invalid validities fall back to 365 with the key reported; 1 and 1 095 are accepted.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1096")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12.5")]
    public void TestCS2_AnInvalidValidityFallsBack(string raw)
    {
        var result = ContinuitySettings.Parse(Rows(raw, "0.5"));

        Assert.Equal(365, result.RestorationTestValidityDays);
        Assert.Equal([Days], result.FallbackApplied);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("1095", 1095)]
    public void TestCS2_TheValidityBoundsAreAccepted(string raw, int expected)
    {
        var result = ContinuitySettings.Parse(Rows(raw, "0.5"));

        Assert.Equal(expected, result.RestorationTestValidityDays);
        Assert.Empty(result.FallbackApplied);
    }

    /// <summary>CS3 — invalid weights fall back to 0.5 with the key reported, a decimal comma included;
    /// 0.01 and 1 are accepted.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-0.5")]
    [InlineData("1.01")]
    [InlineData("abc")]
    [InlineData("0,5")]
    [InlineData("0.505")]
    [InlineData(null)]
    public void TestCS3_AnInvalidWeightFallsBack(string? raw)
    {
        var result = ContinuitySettings.Parse(Rows("365", raw));

        Assert.Equal(0.5m, result.UnverifiedThreatWeight);
        Assert.Equal([Weight], result.FallbackApplied);
    }

    [Theory]
    [InlineData("0.01", "0.01")]
    [InlineData("1", "1")]
    [InlineData("0.500", "0.5")]
    public void TestCS3_TheWeightBoundsAreAccepted(string raw, string expected)
    {
        var result = ContinuitySettings.Parse(Rows("365", raw));

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), result.UnverifiedThreatWeight);
        Assert.Empty(result.FallbackApplied);
    }

    /// <summary>CS4 — the weight is read in the invariant culture even when the process runs in pt-BR,
    /// where the decimal separator is a comma.</summary>
    [Fact]
    public void TestCS4_TheWeightIsReadInTheInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");

            Assert.Equal(0.5m, ContinuitySettings.Parse(Rows("365", "0.5")).UnverifiedThreatWeight);
            Assert.Equal("0.25", ContinuitySettings.FormatWeight(0.25m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    /// <summary>CS5 — a write is refused for the same values, naming the key, and accepted at the bounds.</summary>
    [Theory]
    [InlineData(0, 0.5, Days)]
    [InlineData(1096, 0.5, Days)]
    [InlineData(null, 0.5, Days)]
    [InlineData(365, 0.0, Weight)]
    [InlineData(365, 1.01, Weight)]
    [InlineData(365, 0.505, Weight)]
    [InlineData(365, null, Weight)]
    public void TestCS5_AnOutOfRangeWriteIsRefusedNamingTheKey(int? days, double? weight, string key)
    {
        var ex = Assert.Throws<InvalidParameterException>(() =>
            ContinuitySettings.ValidateForWrite(days, weight is null ? null : (decimal)weight.Value));

        Assert.Equal(key, ex.ParameterName);
    }

    [Theory]
    [InlineData(1, 0.01)]
    [InlineData(1095, 1.0)]
    [InlineData(365, 0.5)]
    public void TestCS5_AWriteAtTheBoundsIsAccepted(int days, double weight)
    {
        ContinuitySettings.ValidateForWrite(days, (decimal)weight);
    }

    /// <summary>CS6 — no damaged row can turn into 0 days (every test stale) or weight 0 (unverified
    /// stops counting).</summary>
    [Theory]
    [InlineData("0", "0")]
    [InlineData("-1", "-1")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void TestCS6_NoInvalidValueBecomesZero(string? days, string? weight)
    {
        var result = ContinuitySettings.Parse(Rows(days, weight));

        Assert.True(result.RestorationTestValidityDays > 0);
        Assert.True(result.UnverifiedThreatWeight > 0m);
    }
}
