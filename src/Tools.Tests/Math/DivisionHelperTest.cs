using JetBrains.Annotations;
using Tools.Math;
using Xunit;

namespace Tools.Tests.Math;

[TestSubject(typeof(DivisionHelper))]
public class DivisionHelperTest
{

    [Theory]
    [InlineData(5, 2, 0.6, 2)]
    [InlineData(7, 3, 0.6, 2)]
    [InlineData(10, 3, 0.6, 3)]
    public void RoundedDivisionTest(double a, double b, double threshold, double expected)
    {
        var result = DivisionHelper.RoundedDivision(a, b, threshold);
        Assert.Equal(expected, result);
    }


}