using System.Collections.Generic;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// T238 (S39 §4.5): the review cadence resolves its band through the one inclusive-lower-bound
/// function. The lookup tables (<c>risk_levels</c>, <c>review_levels</c>) are keyless, so the pure
/// resolver is tested directly, as <c>RiskAcceptancesService.ResolveBand</c> is.
/// </summary>
[TestSubject(typeof(MgmtReviewsService))]
public class MgmtReviewsCadenceBandTest
{
    // Low 0, Medium 4, High 7, Very High 9: the approved register limits (limites_registro).
    private static readonly List<RiskLevel> Levels =
    [
        new() { Value = 0.0m, Name = "Low", DisplayName = "Low", Color = "yellow" },
        new() { Value = 4.0m, Name = "Medium", DisplayName = "Medium", Color = "orange" },
        new() { Value = 7.0m, Name = "High", DisplayName = "High", Color = "orangered" },
        new() { Value = 9.0m, Name = "Very High", DisplayName = "Very High", Color = "red" }
    ];

    private static readonly List<ReviewLevel> Cadences =
    [
        new() { Id = 1, Name = "Very High", Value = 30 },
        new() { Id = 2, Name = "High", Value = 90 },
        new() { Id = 3, Name = "Medium", Value = 120 },
        new() { Id = 4, Name = "Low", Value = 240 }
    ];

    private static int? Cadence(float score, List<RiskLevel>? levels = null) =>
        MgmtReviewsService.ResolveCadenceDays(new RiskScoring { CalculatedRisk = score }, null,
            levels ?? Levels, Cadences);

    [Theory]
    [InlineData(0f, 240)]    // used to resolve to no band at all (strict '>')
    [InlineData(3.99f, 240)]
    [InlineData(4f, 120)]    // used to stay Low
    [InlineData(6.99f, 120)]
    [InlineData(7f, 90)]     // used to stay Medium
    [InlineData(8.99f, 90)]
    [InlineData(9f, 30)]     // used to stay High
    [InlineData(10f, 30)]
    public void ABoundaryScoreGetsTheCadenceOfTheBandItOpens(float score, int expectedDays)
    {
        Assert.Equal(expectedDays, Cadence(score));
    }

    [Fact]
    public void UnorderedLevelsResolveTheSameBand()
    {
        var shuffled = new List<RiskLevel> { Levels[2], Levels[0], Levels[3], Levels[1] };

        Assert.Equal(90, Cadence(7f, shuffled));
    }

    [Fact]
    public void NoConfiguredLevelsMeansNoCadence()
    {
        Assert.Null(Cadence(7f, []));
    }
}
