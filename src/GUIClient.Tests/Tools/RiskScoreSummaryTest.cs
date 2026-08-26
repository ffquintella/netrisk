using System.Globalization;
using GUIClient.Tools;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The inherent → residual line under each row of the risk register (Track 8 milestone 8.2.3), with
/// the business rank from a review campaign (8.6.5).
///
/// The cases that matter are the incomplete ones. Most rows in a real register have an inherent score
/// and nothing else — nobody has computed a residual yet, and no campaign has ranked them — and the
/// line has to read correctly then rather than only when everything is populated.
/// </summary>
public class RiskScoreSummaryTest
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void TestATreatedRiskShowsBothScoresAndTheDelta()
    {
        Assert.Equal("8.0 → 2.0 (−6.0)",
            RiskScoreSummary.Describe(8.0, 2.0, null, Invariant));
    }

    /// <summary>
    /// An untreated risk shows one number, not "8.0 → —". A dash in the residual position reads as a
    /// residual of nothing, when what is true is that nobody has computed one.
    /// </summary>
    [Fact]
    public void TestAnUntreatedRiskShowsOnlyItsInherentScore()
    {
        Assert.Equal("8.0", RiskScoreSummary.Describe(8.0, null, null, Invariant));
    }

    /// <summary>
    /// A negative delta — treatment that made things worse, or a residual computed against a
    /// since-raised inherent — is exactly the row somebody should look at, so the sign is always
    /// rendered.
    /// </summary>
    [Fact]
    public void TestADeltaThatWentTheWrongWayCarriesAPlusSign()
    {
        Assert.Equal("2.0 → 8.0 (+6.0)",
            RiskScoreSummary.Describe(2.0, 8.0, null, Invariant));
    }

    [Fact]
    public void TestAZeroDeltaCarriesNoSign()
    {
        Assert.Equal("5.0 → 5.0 (0.0)",
            RiskScoreSummary.Describe(5.0, 5.0, null, Invariant));
    }

    [Fact]
    public void TestTheBusinessRankIsAppendedWhenACampaignSetOne()
    {
        Assert.Equal("8.0 → 2.0 (−6.0)  ·  #3",
            RiskScoreSummary.Describe(8.0, 2.0, 3, Invariant));
    }

    /// <summary>A ranked but unscored risk still says something.</summary>
    [Fact]
    public void TestARankWithNoScoresStandsAlone()
    {
        Assert.Equal("#1", RiskScoreSummary.Describe(null, null, 1, Invariant));
    }

    /// <summary>
    /// Nothing to say produces nothing. The view binds the whole line's visibility to this being
    /// non-empty, so a placeholder here would put a blank row under every risk in an untreated
    /// register.
    /// </summary>
    [Fact]
    public void TestNothingKnownProducesAnEmptyLine()
    {
        Assert.Equal("", RiskScoreSummary.Describe(null, null, null, Invariant));
    }

    /// <summary>
    /// A residual with no inherent should not happen — the residual is derived from it — but if the
    /// data says so, the line must not silently claim the residual is the inherent score.
    /// </summary>
    [Fact]
    public void TestAResidualWithoutAnInherentScoreIsNotPresentedAsTheScore()
    {
        Assert.Equal("", RiskScoreSummary.Describe(null, 2.0, null, Invariant));
    }

    /// <summary>
    /// Scores are rendered in the running culture, like every other number in the application. A
    /// pt-BR user reading "8.0" where the rest of the window says "8,0" has been shown a number from
    /// somewhere else.
    /// </summary>
    [Fact]
    public void TestTheScoresFollowTheSuppliedCulture()
    {
        var text = RiskScoreSummary.Describe(8.0, 2.5, null, new CultureInfo("pt-BR"));

        Assert.Contains("8,0", text);
        Assert.Contains("2,5", text);
        Assert.DoesNotContain("8.0", text);
    }

    /// <summary>Rounding is to one decimal, so a long float does not push the subject off the row.</summary>
    [Fact]
    public void TestScoresAreRoundedToOneDecimal()
    {
        Assert.Equal("8.3 → 2.7 (−5.6)",
            RiskScoreSummary.Describe(8.2666, 2.7111, null, Invariant));
    }
}
