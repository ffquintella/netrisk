using System.Linq;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex.Read;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M54, T257 (S40 §5.2.2, §8.3, §10.4): <c>RiskIndexBandDto.RampStep</c> is the ramp step the desktop paints a band with
/// (<c>ChartPaletteMap.BandStep</c>), so both follow the spec's table. Spacing the steps evenly gave four bands the steps
/// 1, 2, 4, 5 instead of 1, 3, 4, 5, and the overview disagreed with the pill.
/// </summary>
[TestSubject(typeof(RiskIndexReadService))]
public class RiskIndexBandRampTest
{
    [Theory]
    [InlineData(2, new[] { 1, 5 })]
    [InlineData(3, new[] { 1, 3, 5 })]
    [InlineData(4, new[] { 1, 3, 4, 5 })]
    [InlineData(5, new[] { 1, 2, 3, 4, 5 })]
    public void TestTheRampStepOfEachBandCountIsTheSpecsTable(int count, int[] steps)
    {
        Assert.Equal(steps, Enumerable.Range(0, count).Select(index => RiskIndexReadService.RampStep(index, count)));
    }

    [Fact]
    public void TestTheOverviewBandsOfAFourBandProfileCarryTheTableSteps()
    {
        var parameters = new RiskIndexParameters
        {
            Bands =
            [
                new RiskIndexBandDefinition { LabelPtBr = "Baixo", LabelEnUs = "Low", LowerInclusive = 0 },
                new RiskIndexBandDefinition { LabelPtBr = "Moderado", LabelEnUs = "Moderate", LowerInclusive = 25 },
                new RiskIndexBandDefinition { LabelPtBr = "Alto", LabelEnUs = "High", LowerInclusive = 50 },
                new RiskIndexBandDefinition { LabelPtBr = "Crítico", LabelEnUs = "Critical", LowerInclusive = 75 }
            ]
        };

        var bands = RiskIndexReadService.Bands(parameters);

        Assert.Equal([1, 3, 4, 5], bands.Select(b => b.RampStep));
        Assert.Equal([0, 1, 2, 3], bands.Select(b => b.Index));
    }
}
