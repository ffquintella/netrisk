using System.Linq;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>
/// The criticality labels and pill classes (S38 §3.5). The converters are one-line wrappers over
/// these, so this is where "5 reads as Critical and paints red" is established.
/// </summary>
[TestSubject(typeof(CriticalityScale))]
public class CriticalityScaleTest
{
    [Theory]
    [InlineData(1, "CriticalityVeryLow", "c1", "1")]
    [InlineData(2, "CriticalityLow", "c2", "2")]
    [InlineData(3, "CriticalityMedium", "c3", "3")]
    [InlineData(4, "CriticalityHigh", "c4", "4")]
    [InlineData(5, "CriticalityCritical", "c5", "5")]
    public void EachLevelHasItsLabelClassAndDigit(int level, string key, string cssClass, string digit)
    {
        Assert.Equal(key, CriticalityScale.LabelKey(level));
        Assert.Equal(cssClass, CriticalityScale.ClassFor(level));
        Assert.Equal(digit, CriticalityScale.ShortLabel(level));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void NoLevelOrOneOffTheScaleReadsAsNotSet(int? level)
    {
        Assert.Null(CriticalityScale.Normalize(level));
        Assert.Equal("CriticalityNotSet", CriticalityScale.LabelKey(level));
        Assert.Equal(CriticalityScale.UnsetClass, CriticalityScale.ClassFor(level));
        Assert.Equal(CriticalityScale.UnsetGlyph, CriticalityScale.ShortLabel(level));
    }

    [Fact]
    public void TheLongLabelCarriesTheLevelOnlyWhenThereIsOne()
    {
        Assert.Equal("Critical 5", CriticalityScale.LongLabel(5, "Critical"));
        Assert.Equal("Not set", CriticalityScale.LongLabel(null, "Not set"));
        Assert.Equal("Not set", CriticalityScale.LongLabel(9, "Not set"));
    }

    [Fact]
    public void TheEditDialogOffersNotSetFirstThenOneToFive()
    {
        Assert.Equal(new int?[] { null, 1, 2, 3, 4, 5 }, CriticalityScale.EditableLevels.ToArray());
    }

    [Fact]
    public void AllLabelKeysIsExactlyWhatLabelKeyCanReturn()
    {
        var produced = new int?[] { null, 0, 1, 2, 3, 4, 5, 6 }.Select(CriticalityScale.LabelKey).Distinct().OrderBy(k => k);

        Assert.Equal(produced, CriticalityScale.AllLabelKeys.OrderBy(k => k));
    }

    /// <summary>
    /// The converter resolves these keys at run time, which <c>LocalizationCoverageTest</c>'s literal
    /// scan cannot see — so a missing one would render as "CriticalityHigh" on screen.
    /// </summary>
    [Fact]
    public void EveryLabelKeyIsDeclaredInAllThreeResourceFiles()
    {
        var missing = HostsTestFiles.MissingFromAnyResource(CriticalityScale.AllLabelKeys);

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void EveryPillClassIsStyled()
    {
        var styles = HostsTestFiles.Read("Styles/WindowStyles.axaml");

        foreach (var cssClass in new int?[] { null, 1, 2, 3, 4, 5 }.Select(CriticalityScale.ClassFor))
            Assert.Contains($"Border.criticality.{cssClass}", styles);
    }
}
