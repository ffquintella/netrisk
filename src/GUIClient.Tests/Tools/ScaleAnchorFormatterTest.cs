using System.Globalization;
using GUIClient.Tools;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The rating-time anchor text (Track 8 milestone 8.7.1). Everything asserted here is what a rater
/// reads while choosing a level, so the cases that matter are the incomplete ones: a level with no
/// definition, a level with only one bound, an installation that never configured ranges at all.
/// </summary>
public class ScaleAnchorFormatterTest
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void TestADefinitionWithBothBoundsCarriesItsRange()
    {
        var text = ScaleAnchorFormatter.Describe("Expected within the year", 0.5, 0.9,
            isProbability: true, culture: Invariant);

        // The invariant culture writes a space before the percent sign; the point of passing a
        // culture through at all is that this is the culture's call and not the formatter's.
        Assert.Equal("Expected within the year (50.0 % – 90.0 %)", text);
    }

    [Fact]
    public void TestAnImpactRangeIsRenderedAsMoneyNotAsAPercentage()
    {
        var text = ScaleAnchorFormatter.Describe("Material loss", 100_000, 1_000_000,
            isProbability: false, culture: Invariant);

        Assert.Contains("100,000", text);
        Assert.Contains("1,000,000", text);
        Assert.DoesNotContain("%", text);
    }

    /// <summary>
    /// The seeded scales carry prose for every level but an installation may clear the numbers. A
    /// half-open range is not a range: rendering "5% – " would read as a bound somebody set.
    /// </summary>
    [Theory]
    [InlineData(0.05, null)]
    [InlineData(null, 0.5)]
    [InlineData(null, null)]
    public void TestAPartialRangeIsOmittedRatherThanHalfRendered(double? min, double? max)
    {
        var text = ScaleAnchorFormatter.Describe("Unlikely", min, max, isProbability: true,
            culture: Invariant);

        Assert.Equal("Unlikely", text);
    }

    /// <summary>A range with no prose still says something useful, so it is shown on its own.</summary>
    [Fact]
    public void TestARangeWithoutProseStandsAlone()
    {
        Assert.Equal("1.0 % – 5.0 %",
            ScaleAnchorFormatter.Describe(null, 0.01, 0.05, isProbability: true, culture: Invariant));
    }

    /// <summary>
    /// Nothing configured means nothing shown — the view binds visibility to whether this is blank,
    /// so returning a placeholder here would put an empty labelled block under every choice.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TestAnUnconfiguredLevelProducesNothingToShow(string? definition)
    {
        Assert.Equal("", ScaleAnchorFormatter.Describe(definition, null, null, isProbability: true,
            culture: Invariant));
    }

    [Fact]
    public void TestSurroundingWhitespaceIsTrimmedOffTheProse()
    {
        Assert.Equal("Rare (0.1 % – 1.0 %)",
            ScaleAnchorFormatter.Describe("  Rare  ", 0.001, 0.01, isProbability: true,
                culture: Invariant));
    }

    /// <summary>
    /// The formatter follows the culture it is handed, because the rest of the GUI is localized to
    /// en-US and pt-BR and a Brazilian rater reading "50.0%" where the app says "50,0%" everywhere
    /// else has been shown a number from a different application.
    /// </summary>
    [Fact]
    public void TestTheRangeFollowsTheSuppliedCulture()
    {
        var text = ScaleAnchorFormatter.Describe("Provável", 0.5, 0.9, isProbability: true,
            culture: new CultureInfo("pt-BR"));

        Assert.Contains("50,0", text);
        Assert.DoesNotContain("50.0", text);
    }
}
