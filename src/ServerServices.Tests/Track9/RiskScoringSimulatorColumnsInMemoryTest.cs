using System;
using System.Linq;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Governance;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// M58, T290 (S39 REG-ALE; S47 R3, S48 D1): the residual mean the monetary panel sums survives a classic save. A
/// <c>PUT /Risks/{id}/Scoring</c> whose payload does not carry the simulator's columns — or carries other values — leaves
/// every one of them as the last simulation stored it, and still saves the classic score it was sent for.
/// </summary>
[TestSubject(typeof(RisksService))]
public class RiskScoringSimulatorColumnsInMemoryTest : RiskChainTestBase
{
    private static readonly DateTime Computed = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public RiskScoringSimulatorColumnsInMemoryTest()
    {
        AddRisk(1, UnitA);
        SeedUnscoped(ctx =>
        {
            var s = ctx.RiskScorings.Single(r => r.Id == 1);
            s.ScoringMethod = QuantitativeRiskService.QuantitativeScoringMethod;
            s.QuantLefMin = 0.1; s.QuantLefMostLikely = 0.5; s.QuantLefMax = 2;
            s.QuantLossMin = 10_000; s.QuantLossMostLikely = 50_000; s.QuantLossMax = 400_000;
            s.QuantAleP10 = 1_000; s.QuantAleP50 = 20_000; s.QuantAleP90 = 90_000; s.QuantAleMean = 40_000;
            s.QuantResidualAleP10 = 500; s.QuantResidualAleP50 = 10_000; s.QuantResidualAleP90 = 45_000;
            s.QuantResidualAleMean = 20_000;
            s.QuantLossExceedanceCurve = "[{\"loss\":1,\"probability\":0.5}]";
            s.QuantSeed = 7;
            s.QuantComputedAt = Computed;
        });
    }

    private RiskScoring Stored() => Read(ctx => ctx.RiskScorings.Single(r => r.Id == 1));

    [Fact]
    public void TestAPayloadWithoutTheSimulatorColumnsKeepsTheResidualMean()
    {
#pragma warning disable CS0618 // the synchronous scoring call is the one PUT /Risks/{id}/Scoring drives
        Risks.SaveRiskScoring(new RiskScoring
            { Id = 1, ScoringMethod = 3, CalculatedRisk = 7.5f, ClassicImpact = 4, ClassicLikelihood = 2 });
#pragma warning restore CS0618

        var s = Stored();
        Assert.Equal(20_000, s.QuantResidualAleMean);
        Assert.Equal((10_000.0, 45_000.0, 40_000.0), (s.QuantResidualAleP50!.Value, s.QuantResidualAleP90!.Value, s.QuantAleMean!.Value));
        Assert.Equal((0.5, 50_000.0, 7, Computed), (s.QuantLefMostLikely!.Value, s.QuantLossMostLikely!.Value, s.QuantSeed!.Value,
            s.QuantComputedAt!.Value));
        Assert.NotNull(s.QuantLossExceedanceCurve);

        // The classic fields the save was for are written.
        Assert.Equal((7.5f, 4f, 2f), (s.CalculatedRisk, s.ClassicImpact, s.ClassicLikelihood));
    }

    [Fact]
    public void TestAPayloadCannotForgeTheSimulatorOutputs()
    {
#pragma warning disable CS0618
        Risks.SaveRiskScoring(new RiskScoring
        {
            Id = 1, ScoringMethod = 3, CalculatedRisk = 1f, ClassicImpact = 1, ClassicLikelihood = 1,
            QuantResidualAleMean = 1, QuantAleMean = 2, QuantLefMax = 99, QuantComputedAt = Computed.AddDays(9)
        });
#pragma warning restore CS0618

        var s = Stored();
        Assert.Equal((20_000.0, 40_000.0, 2.0, Computed),
            (s.QuantResidualAleMean!.Value, s.QuantAleMean!.Value, s.QuantLefMax!.Value, s.QuantComputedAt!.Value));
    }

    [Fact]
    public void TestKeepSimulatorColumnsCopiesEveryQuantColumn()
    {
        // Every quant_* property of the entity is in the list: a column added later and forgotten here is blanked again.
        var stored = Stored();
        var incoming = new RiskScoring { Id = 1 };

        RisksService.KeepSimulatorColumns(stored, incoming);

        foreach (var property in typeof(RiskScoring).GetProperties().Where(p => p.Name.StartsWith("Quant", StringComparison.Ordinal)))
            Assert.Equal(property.GetValue(stored), property.GetValue(incoming));
    }
}
