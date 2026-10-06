using JetBrains.Annotations;
using Tools.Risks;
using Xunit;

namespace Tools.Tests.Risks;

/// <summary>
/// Stage 9.2 (S42 §8, M1–M7) — the duplicate rule for risk scenarios: the same central event
/// <b>and</b> the same consequences, compared after a shallow, deterministic normalization.
/// </summary>
[TestSubject(typeof(RiskScenarioMatcher))]
public class RiskScenarioMatcherTest
{
    /// <summary>M1 — case, accents, whitespace runs and trailing punctuation do not make a scenario different.</summary>
    [Theory]
    [InlineData("Indisponibilidade do portal acadêmico", "indisponibilidade do portal academico")]
    [InlineData("  Portal   unavailable\t", "portal unavailable")]
    [InlineData("Portal unavailable.", "portal unavailable")]
    [InlineData("Portal unavailable ;", "portal unavailable")]
    [InlineData("Data lost . .", "data lost")]
    [InlineData("ÇÃO", "cao")]
    public void TestM1_NormalizationIgnoresCaseAccentsSpacingAndTrailingPunctuation(string text, string expected)
    {
        Assert.Equal(expected, RiskScenarioMatcher.Normalize(text));
    }

    /// <summary>M2 — words still matter: a rephrasing is a different text (fuzzy matching is out, S42 §11 D7).</summary>
    [Fact]
    public void TestM2_ARephrasingIsNotNormalizedAway()
    {
        Assert.NotEqual(RiskScenarioMatcher.Normalize("Portal unavailable"),
            RiskScenarioMatcher.Normalize("Portal is down"));
        Assert.NotEqual(RiskScenarioMatcher.Normalize("Portal, unavailable"),
            RiskScenarioMatcher.Normalize("Portal unavailable"));
    }

    /// <summary>M3 — nothing to compare is <c>null</c>, never an empty string that would equal another empty string.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" . ")]
    public void TestM3_EmptyTextNormalizesToNull(string? text)
    {
        Assert.Null(RiskScenarioMatcher.Normalize(text));
    }

    /// <summary>M4 — same event and same consequences: a duplicate.</summary>
    [Fact]
    public void TestM4_TheSamePairIsADuplicate()
    {
        Assert.True(RiskScenarioMatcher.IsDuplicate(
            "Ransomware encrypts the file server", "Payroll cannot run.",
            "ransomware encrypts the FILE server", "payroll cannot run"));
    }

    /// <summary>
    /// M5 — the methodology's edge case: the same central event with different consequences is two
    /// scenarios, and so is the same consequence of two different events.
    /// </summary>
    [Fact]
    public void TestM5_OneHalfInCommonIsNotADuplicate()
    {
        Assert.False(RiskScenarioMatcher.IsDuplicate(
            "Portal unavailable", "Enrolments lost",
            "Portal unavailable", "Reputational damage"));
        Assert.False(RiskScenarioMatcher.IsDuplicate(
            "Portal unavailable", "Enrolments lost",
            "Payment gateway unavailable", "Enrolments lost"));
    }

    /// <summary>M6 — a missing half never matches, not even another missing half (two legacy risks).</summary>
    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("Portal unavailable", null, "Portal unavailable", null)]
    [InlineData(null, "Enrolments lost", null, "Enrolments lost")]
    [InlineData("", "", "", "")]
    public void TestM6_AMissingHalfNeverMatches(string? eventA, string? consA, string? eventB, string? consB)
    {
        Assert.False(RiskScenarioMatcher.IsDuplicate(eventA, consA, eventB, consB));
        Assert.Null(RiskScenarioMatcher.Key(eventA, consA));
    }

    /// <summary>
    /// M7 — the key is a pair, so text that moves between the two halves cannot collide the way a
    /// joined string with a separator could.
    /// </summary>
    [Fact]
    public void TestM7_TextMovingBetweenTheHalvesIsNotADuplicate()
    {
        Assert.False(RiskScenarioMatcher.IsDuplicate("a b", "c", "a", "b c"));
    }
}
