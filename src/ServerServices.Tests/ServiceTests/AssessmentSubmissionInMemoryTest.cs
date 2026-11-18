using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Assessments;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// M58, T280 (S39 D-16): an answer links to its option by id, a run records when it was submitted and the score the
/// server computed then, and later saves move neither the submission nor <c>RunDate</c>.
/// </summary>
[TestSubject(typeof(AssessmentsService))]
public class AssessmentSubmissionInMemoryTest : InMemoryServiceTestBase
{
    private const int AssessmentId = 1, OtherAssessment = 2, EntityId = 1, Analyst = 2, RunId = 10;
    private const int Q1 = 101, Q2 = 102, Q3 = 103, QOther = 201;
    private static readonly DateTime Created = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly IAssessmentsService _svc;

    public AssessmentSubmissionInMemoryTest()
    {
        _svc = GetService<IAssessmentsService>();
        Seed(ctx =>
        {
            ctx.Assessments.Add(new Assessment { Id = AssessmentId, Name = "ISO", Created = Created });
            ctx.Assessments.Add(new Assessment { Id = OtherAssessment, Name = "Other", Created = Created });
            ctx.Entities.Add(new Entity
            {
                Id = EntityId, DefinitionName = "organization", DefinitionVersion = "1", Status = "active", Created = Created,
                Updated = Created
            });
            ctx.Users.Add(new User { Value = Analyst, Name = "U", Type = "local", Enabled = true, Email = "u@x.io", Password = [1] });

            ctx.AssessmentQuestions.Add(Question(Q1, AssessmentId));
            ctx.AssessmentQuestions.Add(Question(Q2, AssessmentId));
            // Shown only when Q1 is answered "No".
            ctx.AssessmentQuestions.Add(Question(Q3, AssessmentId,
                "{\"QuestionId\": 101, \"Operator\": \"equals\", \"Value\": \"No\"}"));
            ctx.AssessmentQuestions.Add(Question(QOther, OtherAssessment));

            ctx.AssessmentAnswers.Add(Option(1, Q1, "Yes", 0));
            ctx.AssessmentAnswers.Add(Option(2, Q1, "No", 10));
            ctx.AssessmentAnswers.Add(Option(3, Q2, "Partly", 4));
            ctx.AssessmentAnswers.Add(Option(4, Q2, "Never", 8));
            ctx.AssessmentAnswers.Add(Option(5, Q2, "never", 8));
            ctx.AssessmentAnswers.Add(Option(6, Q3, "Weak", 5));
            ctx.AssessmentAnswers.Add(Option(7, Q3, "Strong", 0));
            ctx.AssessmentAnswers.Add(Option(8, QOther, "Yes", 1, OtherAssessment));

            ctx.AssessmentRuns.Add(new AssessmentRun
            {
                Id = RunId, AssessmentId = AssessmentId, EntityId = EntityId, AnalystId = Analyst, RunDate = Created,
                Status = (int)AssessmentStatus.Open
            });
        });
    }

    private static AssessmentQuestion Question(int id, int assessment, string? condition = null) => new()
    {
        Id = id, AssessmentId = assessment, Question = $"Q{id}", Order = id, PageNumber = 1, ConditionJson = condition
    };

    private static AssessmentAnswer Option(int id, int question, string text, float score, int assessment = AssessmentId) => new()
    {
        Id = id, AssessmentId = assessment, QuestionId = question, Answer = text, RiskScore = score,
        RiskSubject = [], Order = id
    };

    private AssessmentRun StoredRun()
    {
        using var db = OpenContext();
        return db.AssessmentRuns.Single(r => r.Id == RunId);
    }

    private AssessmentRunAnswer StoredAnswer(int question)
    {
        using var db = OpenContext();
        return db.AssessmentRunAnswers.Single(a => a.AssessmentRunId == RunId && a.AssessmentQuestionId == question);
    }

    /// <summary>The save the desktop makes: no host, the run's own analyst, the status it wants.</summary>
    private static AssessmentRunDto Save(AssessmentStatus? status, string? comments = null) => new()
    {
        Id = RunId, AssessmentId = AssessmentId, EntityId = EntityId, AnalystId = Analyst, HostId = null,
        Status = status is null ? null : (int)status, Comments = comments, RunDate = new DateTime(2030, 1, 1)
    };

    [Fact]
    public async Task TestAnAnswerIsLinkedToTheOptionTheClientNames()
    {
        await _svc.SaveDraftAnswerAsync(RunId, Q2, "\"Never\"", assessmentAnswerId: 4);

        Assert.Equal(4, StoredAnswer(Q2).AssessmentAnswerId);
    }

    /// <summary>Older clients send only the text: linked when exactly one option carries it, case aside.</summary>
    [Theory]
    [InlineData(Q1, "\"no\"", 2)]
    [InlineData(Q1, "Yes", 1)]
    [InlineData(Q2, "\"NEVER\"", null)]
    [InlineData(Q2, "\"Sometimes\"", null)]
    [InlineData(Q2, "\"\"", null)]
    public async Task TestWithoutAnIdTheAnswerIsLinkedByItsUniqueText(int question, string content, int? expected)
    {
        await _svc.SaveDraftAnswerAsync(RunId, question, content);

        Assert.Equal(expected, StoredAnswer(question).AssessmentAnswerId);
    }

    [Fact]
    public async Task TestChangingTheAnswerMovesTheLink()
    {
        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"Yes\"", 1);
        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"No\"", 2);
        Assert.Equal(2, StoredAnswer(Q1).AssessmentAnswerId);

        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"\"");
        Assert.Null(StoredAnswer(Q1).AssessmentAnswerId);
    }

    [Fact]
    public async Task TestAnOptionOfAnotherQuestionOrAQuestionOfAnotherAssessmentIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.SaveDraftAnswerAsync(RunId, Q1, "\"Partly\"", 3));
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.SaveDraftAnswerAsync(RunId, QOther, "\"Yes\"", 8));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.SaveDraftAnswerAsync(999, Q1, "\"Yes\"", 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.SaveDraftAnswerAsync(RunId, 999, "\"Yes\""));

        using var db = OpenContext();
        Assert.Empty(db.AssessmentRunAnswers);
    }

    /// <summary>
    /// Submitting stamps the UTC submission time and records the score over the visible questions: Q1 "No" (10 of 10) shows
    /// Q3, answered "Weak" (5 of 5); Q2 "Partly" (4 of 8) — 19 of 23.
    /// </summary>
    [Fact]
    public async Task TestSubmittingRecordsTheSubmissionTimeAndTheServersScore()
    {
        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"No\"", 2);
        await _svc.SaveDraftAnswerAsync(RunId, Q2, "\"Partly\"", 3);
        await _svc.SaveDraftAnswerAsync(RunId, Q3, "\"Weak\"", 6);

        var before = DateTime.UtcNow.AddSeconds(-1);
        _svc.UpdateRun(Save(AssessmentStatus.Submitted));

        var run = StoredRun();
        Assert.Equal((int)AssessmentStatus.Submitted, run.Status);
        Assert.InRange(run.SubmittedAt!.Value, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(19.0 / 23.0, run.Score!.Value, 9);
    }

    /// <summary>An answer left on a question its condition hides does not count: Q1 "Yes" hides Q3.</summary>
    [Fact]
    public async Task TestAnAnswerOnAHiddenQuestionIsNotScored()
    {
        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"Yes\"", 1);
        await _svc.SaveDraftAnswerAsync(RunId, Q2, "\"Never\"", 4);
        await _svc.SaveDraftAnswerAsync(RunId, Q3, "\"Weak\"", 6);

        _svc.UpdateRun(Save(AssessmentStatus.Submitted));

        Assert.Equal(8.0 / 18.0, StoredRun().Score!.Value, 9);
    }

    [Fact]
    public void TestARunWithNothingScorableHasNoScore()
    {
        _svc.UpdateRun(Save(AssessmentStatus.Submitted));

        Assert.NotNull(StoredRun().SubmittedAt);
        Assert.Null(StoredRun().Score);
    }

    /// <summary>
    /// Regression (D-16): every save overwrote RunDate with the server's local now, so "run date" meant "last touched". A
    /// later save of a submitted run — a comment, a missing status — keeps the run submitted, its submission time, its
    /// score and its RunDate, and ignores the RunDate the client sends.
    /// </summary>
    [Fact]
    public async Task TestALaterSaveMovesNeitherTheSubmissionNorTheRunDate()
    {
        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"No\"", 2);
        _svc.UpdateRun(Save(AssessmentStatus.Submitted));
        var submitted = StoredRun();

        await Task.Delay(20);
        _svc.UpdateRun(Save(AssessmentStatus.Submitted, "a comment"));
        _svc.UpdateRun(Save(null, "another comment"));

        var after = StoredRun();
        Assert.Equal((int)AssessmentStatus.Submitted, after.Status);
        Assert.Equal(submitted.SubmittedAt, after.SubmittedAt);
        Assert.Equal(submitted.Score, after.Score);
        Assert.Equal(Created, after.RunDate);
        Assert.Equal("another comment", after.Comments);
    }

    [Fact]
    public async Task TestReopeningClearsTheSubmissionAndResubmittingStampsItAgain()
    {
        await _svc.SaveDraftAnswerAsync(RunId, Q1, "\"No\"", 2);
        _svc.UpdateRun(Save(AssessmentStatus.Submitted));
        var first = StoredRun().SubmittedAt;

        _svc.UpdateRun(Save(AssessmentStatus.Open));
        Assert.Null(StoredRun().SubmittedAt);
        Assert.Null(StoredRun().Score);

        await Task.Delay(20);
        _svc.UpdateRun(Save(AssessmentStatus.Submitted));
        Assert.True(StoredRun().SubmittedAt > first);
        Assert.Equal(1.0, StoredRun().Score);
    }

    /// <summary>Regression: a run without a host could not be saved at all ("host not found").</summary>
    [Fact]
    public void TestARunWithoutAHostIsSaved()
    {
        _svc.UpdateRun(Save(AssessmentStatus.Open, "no host"));

        Assert.Equal("no host", StoredRun().Comments);
        Assert.Null(StoredRun().HostId);
    }

    /// <summary>The report the backfill leaves: unlinked answers with text, and why each has no option.</summary>
    [Fact]
    public async Task TestTheUnlinkedAnswersAreReportedWithTheirReason()
    {
        Seed(ctx =>
        {
            ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
                { Id = 1, AssessmentRunId = RunId, AssessmentQuestionId = Q1, AnswerContentJson = "\"Maybe\"" });
            ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
                { Id = 2, AssessmentRunId = RunId, AssessmentQuestionId = Q2, AnswerContentJson = "\"never\"" });
            ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
                { Id = 3, AssessmentRunId = RunId, AssessmentQuestionId = Q3, AnswerContentJson = "\"Weak\"" });
            ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
                { Id = 4, AssessmentRunId = RunId, AssessmentQuestionId = Q1, AnswerContentJson = "\"No\"", AssessmentAnswerId = 2 });
            ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
                { Id = 5, AssessmentRunId = RunId, AssessmentQuestionId = Q2, AnswerContentJson = null, Comment = "only a comment" });
        });

        var gaps = await _svc.GetUnlinkedAnswersAsync();

        Assert.Equal(
            new[] { (1, AssessmentAnswerLinkGap.NoMatch), (2, AssessmentAnswerLinkGap.Ambiguous), (3, AssessmentAnswerLinkGap.Matchable) },
            gaps.Select(g => (g.RunAnswerId, g.Reason)).ToArray());
        Assert.All(gaps, g => Assert.Equal((RunId, AssessmentId), (g.RunId, g.AssessmentId)));
        Assert.Equal("Maybe", gaps[0].AnswerText);
    }
}
