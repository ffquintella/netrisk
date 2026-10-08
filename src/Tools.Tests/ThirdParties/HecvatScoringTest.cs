using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.ThirdParties;
using Tools.ThirdParties;
using Xunit;

namespace Tools.Tests.ThirdParties;

/// <summary>
/// Stage 9.10 (S51 §4.5, D7, §8 H1–H11) — the HECVAT score. The edge case the methodology names for this stage is H2: a
/// partially answered questionnaire scores <c>Incomplete</c>, carries no score, and is never conforming — however good the
/// part that was answered.
/// </summary>
[TestSubject(typeof(HecvatScoring))]
public class HecvatScoringTest
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static HecvatAnswerFacts Yes(string id, int weight = 1, bool critical = false) =>
        new(id, HecvatAnswer.Yes, HecvatAnswer.Yes, weight, critical);

    private static HecvatAnswerFacts No(string id, int weight = 1, bool critical = false) =>
        new(id, HecvatAnswer.No, HecvatAnswer.Yes, weight, critical);

    private static HecvatAnswerFacts Na(string id, bool critical = false) =>
        new(id, HecvatAnswer.NotApplicable, HecvatAnswer.Yes, 1, critical);

    private static HecvatAnswerFacts Blank(string id) => new(id, HecvatAnswer.Unanswered, HecvatAnswer.Yes, 1, false);

    private static List<HecvatAnswerFacts> Perfect(int count) =>
        Enumerable.Range(1, count).Select(i => Yes($"Q-{i:00}")).ToList();

    private static HecvatResultDto Score(int expected, IReadOnlyCollection<HecvatAnswerFacts> answers,
        DateTime? validUntil = null, bool voided = false) =>
        HecvatScoring.Score(expected, answers, validUntil, voided, Now);

    /// <summary>H1 — complete, every scored question as preferred: conforming at 100 %.</summary>
    [Fact]
    public void TestH1_ACompletePerfectQuestionnaireConforms()
    {
        var result = Score(10, Perfect(10));

        Assert.Equal(HecvatState.Conforming, result.State);
        Assert.Equal(1m, result.Score);
        Assert.Equal((10, 10, 0, 0), (result.ExpectedCount, result.AnsweredCount, result.UnansweredCount, result.NotApplicableCount));
        Assert.Equal(ThirdPartyLimits.HecvatPassThreshold, result.PassThreshold);
    }

    /// <summary>
    /// H2 (T205) — partial is incomplete, never a pass, and carries no score: 99 of 100 perfect answers, a blank question
    /// with every count reached, nothing answered at all, and a single answer of a hundred.
    /// </summary>
    [Fact]
    public void TestH2_APartialQuestionnaireIsIncompleteAndNeverAPass()
    {
        var shapes = new Dictionary<string, (int Expected, List<HecvatAnswerFacts> Answers)>
        {
            ["99 of 100, all as preferred"] = (100, Perfect(99)),
            ["all expected rows, one left blank"] = (3, [Yes("A-01"), Yes("A-02"), Blank("A-03")]),
            ["the expected count reached, plus a blank row"] = (2, [Yes("A-01"), Yes("A-02"), Blank("A-03")]),
            ["nothing answered"] = (40, []),
            ["one of a hundred"] = (100, [Yes("A-01")])
        };

        foreach (var (name, (expected, answers)) in shapes)
        {
            var result = Score(expected, answers);

            Assert.True(result.State == HecvatState.Incomplete, $"{name}: {result.State}");
            Assert.True(result.Score is null, $"{name}: a score of {result.Score} was reported for a partial questionnaire");
            Assert.NotEqual(HecvatState.Conforming, result.State);
            Assert.Contains("never passes", result.Explanation);
        }

        var partial = Score(100, Perfect(99));
        Assert.Equal((99, 1), (partial.AnsweredCount, partial.UnansweredCount));
        Assert.Equal(new[] { "A-03" }, Score(3, [Yes("A-01"), Yes("A-02"), Blank("a-03")]).BlankQuestionIds);
    }

    /// <summary>H3 — the threshold is inclusive: 80 % conforms, 79 % does not.</summary>
    [Theory]
    [InlineData(8, HecvatState.Conforming)]
    [InlineData(7, HecvatState.NonConforming)]
    public void TestH3_TheThresholdIsInclusive(int asPreferred, HecvatState expected)
    {
        var answers = Enumerable.Range(1, 10)
            .Select(i => i <= asPreferred ? Yes($"T-{i:00}") : No($"T-{i:00}"))
            .ToList();

        var result = Score(10, answers);

        Assert.Equal(expected, result.State);
        Assert.Equal(asPreferred / 10m, result.Score);
    }

    /// <summary>H4 — a critical question against the preference makes it non-conforming whatever the score.</summary>
    [Fact]
    public void TestH4_ACriticalFailureIsNonConformingWhateverTheScore()
    {
        var answers = Perfect(19);
        answers.Add(No("MFA-01", critical: true));

        var result = Score(20, answers);

        Assert.Equal(HecvatState.NonConforming, result.State);
        Assert.Equal(0.95m, result.Score);
        Assert.Equal(new[] { "MFA-01" }, result.CriticalFailures);
    }

    /// <summary>H5 — N/A is out of the denominator; a critical question answered N/A is not a failure.</summary>
    [Fact]
    public void TestH5_NotApplicableIsOutOfTheDenominator()
    {
        var result = Score(5, [Yes("A-01"), Yes("A-02"), Yes("A-03"), Yes("A-04"), Na("A-05", critical: true)]);

        Assert.Equal(HecvatState.Conforming, result.State);
        Assert.Equal(1m, result.Score);
        Assert.Equal(1, result.NotApplicableCount);
        Assert.Empty(result.CriticalFailures);
    }

    /// <summary>H6 — an informational question is not scored; with nothing scored the questionnaire is not scorable, not a pass.</summary>
    [Fact]
    public void TestH6_NothingScoredIsNotScorable()
    {
        var informational = new List<HecvatAnswerFacts>
        {
            new("I-01", HecvatAnswer.Yes, null, 1, false), new("I-02", HecvatAnswer.No, null, 1, false)
        };
        Assert.Equal(HecvatState.NotScorable, Score(2, informational).State);
        Assert.Null(Score(2, informational).Score);

        Assert.Equal(HecvatState.NotScorable, Score(2, [Na("A-01"), Na("A-02")]).State);

        // Informational questions do not dilute a scored one.
        var mixed = Score(3, [Yes("A-01"), new("I-01", HecvatAnswer.No, null, 50, false), new("I-02", HecvatAnswer.No, null, 1, false)]);
        Assert.Equal((HecvatState.Conforming, 1m), (mixed.State, mixed.Score));
    }

    /// <summary>H7 — past its validity it is expired, never a pass, its score still shown; the day it ends it still counts.</summary>
    [Fact]
    public void TestH7_AnExpiredQuestionnaireNeverPasses()
    {
        var expired = Score(10, Perfect(10), validUntil: Now.AddSeconds(-1));
        Assert.Equal((HecvatState.Expired, 1m), (expired.State, expired.Score));

        Assert.Equal(HecvatState.Conforming, Score(10, Perfect(10), validUntil: Now).State);

        // Incomplete wins over expired: the missing answers are the first thing to fix.
        Assert.Equal(HecvatState.Incomplete, Score(10, Perfect(9), validUntil: Now.AddDays(-1)).State);
    }

    /// <summary>H8 — a voided questionnaire reads as nothing.</summary>
    [Fact]
    public void TestH8_AVoidedQuestionnaireReadsAsNothing()
    {
        var result = Score(10, Perfect(10), voided: true);

        Assert.Equal(HecvatState.Voided, result.State);
        Assert.Null(result.Score);
    }

    /// <summary>H9 — the score is by weight: one heavy failure outweighs many light successes.</summary>
    [Fact]
    public void TestH9_TheScoreIsWeighted()
    {
        var answers = Perfect(10);
        answers.Add(No("ENC-01", weight: 10));

        var result = Score(11, answers);

        Assert.Equal(0.5m, result.Score);
        Assert.Equal(HecvatState.NonConforming, result.State);
    }

    /// <summary>H10 — one answer per question id, case-insensitively: a repeated id cannot inflate the answered count.</summary>
    [Fact]
    public void TestH10_ARepeatedQuestionIdCountsOnce()
    {
        var result = Score(2, [Yes("A-01"), Yes("a-01")]);

        Assert.Equal(1, result.AnsweredCount);
        Assert.Equal(HecvatState.Incomplete, result.State);
    }

    /// <summary>H11 — the preferred answer may be No: answering No to "do you share data with third parties?" is the pass.</summary>
    [Fact]
    public void TestH11_ThePreferredAnswerMayBeNo()
    {
        var result = Score(2, [new("D-01", HecvatAnswer.No, HecvatAnswer.No, 1, true), Yes("D-02")]);

        Assert.Equal((HecvatState.Conforming, 1m), (result.State, result.Score));
        Assert.Empty(result.CriticalFailures);
    }
}
