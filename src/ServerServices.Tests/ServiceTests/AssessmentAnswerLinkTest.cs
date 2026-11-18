using System.Collections.Generic;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Assessments;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// M58, T280 (S39 §3.1, D-16): the rule that names an answer's option — by id, else by unique text — and the score of a
/// submitted run. The version 114 backfill applies the same text rule in SQL (<c>AssessmentAnswerLinkBackfillTest</c>).
/// </summary>
[TestSubject(typeof(AssessmentAnswerLink))]
public class AssessmentAnswerLinkTest
{
    private static AssessmentAnswer Option(int id, int question, string text, float score) => new()
        { Id = id, AssessmentId = 1, QuestionId = question, Answer = text, RiskScore = score, RiskSubject = [] };

    private static readonly AssessmentAnswer[] Options =
    [
        Option(1, 1, "Yes", 0), Option(2, 1, "No", 10),
        Option(3, 2, "Never", 8), Option(4, 2, "never", 8), Option(5, 2, "Partly", 4)
    ];

    [Theory]
    [InlineData("\"Yes\"", "Yes")]
    [InlineData("\"Say \\\"no\\\"\"", "Say \"no\"")]
    [InlineData("Yes", "Yes")]
    [InlineData("\"\"", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("{\"value\":\"Yes\"}", "{\"value\":\"Yes\"}")]
    [InlineData("5", "5")]
    public void TestDecodeTextReadsTheJsonStringOrTheBareText(string? content, string expected)
    {
        Assert.Equal(expected, AssessmentAnswerLink.DecodeText(content));
    }

    [Fact]
    public void TestMatchByTextIsUniqueAndCaseInsensitive()
    {
        var (match, none) = AssessmentAnswerLink.MatchByText(Options[..2], "no");
        Assert.Equal(2, match!.Id);
        Assert.Null(none);

        Assert.Equal(AssessmentAnswerLink.Unmatched.Ambiguous, Reason(Options[2..], "NEVER"));
        Assert.Equal(AssessmentAnswerLink.Unmatched.NoMatch, Reason(Options[2..], "Always"));
        Assert.Equal(AssessmentAnswerLink.Unmatched.NoMatch, Reason(Options, ""));

        static AssessmentAnswerLink.Unmatched? Reason(AssessmentAnswer[] options, string text)
        {
            var (option, reason) = AssessmentAnswerLink.MatchByText(options, text);
            Assert.Null(option);
            return reason;
        }
    }

    /// <summary>The id wins over the text; an id that is not among the options names nothing (no fallback to text).</summary>
    [Fact]
    public void TestResolvePrefersTheIdAndNeverGuessesPastAStaleOne()
    {
        Assert.Equal(1, AssessmentAnswerLink.Resolve(Options[..2], 1, "No")!.Id);
        Assert.Equal(2, AssessmentAnswerLink.Resolve(Options[..2], null, "No")!.Id);
        Assert.Null(AssessmentAnswerLink.Resolve(Options[..2], 99, "No"));
        Assert.Null(AssessmentAnswerLink.Resolve(Options[2..], null, "never"));
    }

    [Fact]
    public void TestTheScoreIsChosenOverMaximumOnTheVisibleAnsweredQuestions()
    {
        var chosen = new Dictionary<int, int> { [1] = 2, [2] = 5 };

        // 10 of 10 and 4 of 8.
        Assert.Equal(14.0 / 18.0, AssessmentRunScore.Compute([1, 2], chosen, Options)!.Value, 9);
        // Question 2 hidden: only question 1 counts.
        Assert.Equal(1.0, AssessmentRunScore.Compute([1], chosen, Options));
        // A visible question without an answer does not count; nothing scorable is no score, not 0.
        Assert.Null(AssessmentRunScore.Compute([1, 2], new Dictionary<int, int>(), Options));
        Assert.Null(AssessmentRunScore.Compute([1], new Dictionary<int, int> { [1] = 1 }, [Option(1, 1, "Yes", 0)]));
        // An option id that is not the question's is ignored.
        Assert.Null(AssessmentRunScore.Compute([1], new Dictionary<int, int> { [1] = 3 }, Options));
    }

    [Fact]
    public void TestNegativeScoresCountAsZero()
    {
        AssessmentAnswer[] options = [Option(1, 1, "A", -5), Option(2, 1, "B", 4)];

        Assert.Equal(0.0, AssessmentRunScore.Compute([1], new Dictionary<int, int> { [1] = 1 }, options));
    }
}
