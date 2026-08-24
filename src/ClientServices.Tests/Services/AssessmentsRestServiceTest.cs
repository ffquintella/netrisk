using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.EntitiesDto;
using JetBrains.Annotations;
using Model.Assessments;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers <see cref="AssessmentsRestService"/> against a programmable HTTP backend: assessments,
/// runs, questions, answers, run answers, the run-viewer draft endpoints and template import.
/// </summary>
[TestSubject(typeof(AssessmentsRestService))]
public class AssessmentsRestServiceTest : BaseServiceTest
{
    private static readonly DateTime FixedDate = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly StubRestBackend _backend = new();
    private readonly IAssessmentsService _service;

    public AssessmentsRestServiceTest()
    {
        _service = ResolveWith<IAssessmentsService>(_backend);
    }

    private static List<Assessment> TwoAssessments() =>
    [
        new() { Id = 1, Name = "Vendor review", Created = FixedDate, EntityId = 3 },
        new() { Id = 2, Name = "Yearly audit", Created = FixedDate }
    ];

    private static AssessmentAnswer Answer(int id, string text) => new()
    {
        Id = id, AssessmentId = 1, QuestionId = 2, Answer = text, Order = id,
        RiskSubject = [], AssessmentScoringId = 1
    };

    /// <summary>An unsaved answer: no id, and no owning assessment/question yet.</summary>
    private static AssessmentAnswer NewAnswer(string text) => new()
    {
        Id = 0, AssessmentId = 0, QuestionId = 0, Answer = text, RiskSubject = [], AssessmentScoringId = 1
    };

    private static string WriteTempTemplate(string content = "{\"pages\":[]}")
    {
        var path = Path.Combine(Path.GetTempPath(), $"netrisk-template-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    // -------------------------------------------------------------- GetAssessmentsAsync

    [Fact]
    public async Task TestGetAssessmentsAsync()
    {
        _backend.OnGet("/Assessments", TwoAssessments());

        var assessments = await _service.GetAssessmentsAsync();

        Assert.NotNull(assessments);
        Assert.Equal(2, assessments.Count);
        Assert.Equal("Vendor review", assessments[0].Name);
        Assert.Equal(3, assessments[0].EntityId);
        Assert.Equal("GET /Assessments", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAssessmentsAsyncReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetAssessmentsAsync());
    }

    [Fact]
    public async Task TestGetAssessmentsAsyncReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Get, "/Assessments", HttpStatusCode.InternalServerError);

        Assert.Null(await _service.GetAssessmentsAsync());
    }

    [Fact]
    public async Task TestGetAssessmentsAsyncReturnsNullOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Assessments");

        Assert.Null(await _service.GetAssessmentsAsync());
    }

    // ---------------------------------------------------------------- GetAssessmentRuns

    [Fact]
    public void TestGetAssessmentRuns()
    {
        _backend.OnGet("/Assessments/1/Runs", new List<AssessmentRun>
        {
            new()
            {
                Id = 10, AssessmentId = 1, EntityId = 3, AnalystId = 4, Status = 1,
                ProgressPercentage = 50, CurrentPageIndex = 2, Comments = "half way",
                RunDate = FixedDate
            },
            new() { Id = 11, AssessmentId = 1, EntityId = 3, Status = 0 }
        });

        var runs = _service.GetAssessmentRuns(1);

        Assert.NotNull(runs);
        Assert.Equal(2, runs.Count);
        Assert.Equal(50, runs[0].ProgressPercentage);
        Assert.Equal("half way", runs[0].Comments);
        Assert.Equal(2, runs[0].CurrentPageIndex);
        Assert.Equal("GET /Assessments/1/Runs", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAssessmentRunsReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Runs", HttpStatusCode.NotFound);

        Assert.Null(_service.GetAssessmentRuns(1));
    }

    [Fact]
    public void TestGetAssessmentRunsReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Runs", HttpStatusCode.InternalServerError);

        Assert.Null(_service.GetAssessmentRuns(1));
    }

    // -------------------------------------------------------------- UpdateAssessmentRun

    [Fact]
    public void TestUpdateAssessmentRun()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Runs/10", HttpStatusCode.OK);

        _service.UpdateAssessmentRun(new AssessmentRunDto
        {
            Id = 10, AssessmentId = 1, EntityId = 3, Comments = "reviewed", ProgressPercentage = 100
        });

        Assert.Equal("PUT /Assessments/1/Runs/10", _backend.LastRequest.ToString());
        Assert.Contains("reviewed", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateAssessmentRunSwallowsANonOkStatus()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Runs/10", HttpStatusCode.NotFound);

        // The method only logs the failure — the GUI is not told about it.
        _service.UpdateAssessmentRun(new AssessmentRunDto { Id = 10, AssessmentId = 1, EntityId = 3 });

        Assert.True(_backend.Sent(Method.Put, "/Assessments/1/Runs/10"));
    }

    [Fact]
    public void TestUpdateAssessmentRunSwallowsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Assessments/1/Runs/10");

        _service.UpdateAssessmentRun(new AssessmentRunDto { Id = 10, AssessmentId = 1, EntityId = 3 });

        Assert.True(_backend.Sent(Method.Put, "/Assessments/1/Runs/10"));
    }

    // ------------------------------------------------------------------ DeleteAllAnswers

    [Fact]
    public void TestDeleteAllAnswers()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Runs/10/answers", HttpStatusCode.OK);

        _service.DeleteAllAnswers(1, 10);

        Assert.Equal("DELETE /Assessments/1/Runs/10/answers", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteAllAnswersSwallowsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Runs/10/answers", HttpStatusCode.InternalServerError);

        _service.DeleteAllAnswers(1, 10);

        Assert.True(_backend.Sent(Method.Delete, "/Assessments/1/Runs/10/answers"));
    }

    // ------------------------------------------------------------------------- DeleteRun

    [Fact]
    public void TestDeleteRun()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Runs/10", HttpStatusCode.OK);

        _service.DeleteRun(1, 10);

        Assert.Equal("DELETE /Assessments/1/Runs/10", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteRunThrowsOnANonOkStatus()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Runs/10", HttpStatusCode.NotFound);

        // Known limitation: the RestException raised inside the try is caught by the method's own
        // catch-all, so every failure surfaces as this bare Exception and the status is lost.
        var ex = Assert.Throws<Exception>(() => _service.DeleteRun(1, 10));
        Assert.Equal("unknown error deleting assessment run", ex.Message);
    }

    [Fact]
    public void TestDeleteRunThrowsOnAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Runs/10", HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<Exception>(() => _service.DeleteRun(1, 10));
        Assert.Equal("unknown error deleting assessment run", ex.Message);
    }

    // ---------------------------------------------------------- GetAssessmentRunAnsers

    [Fact]
    public void TestGetAssessmentRunAnsers()
    {
        _backend.OnGet("/Assessments/1/Runs/10/Answers", new List<AssessmentRunsAnswer>
        {
            new() { Id = 1, AnswerId = 5, QuestionId = 2, RunId = 10 },
            new() { Id = 2, AnswerId = 6, QuestionId = 3, RunId = 10 }
        });

        var answers = _service.GetAssessmentRunAnsers(1, 10);

        Assert.NotNull(answers);
        Assert.Equal(2, answers.Count);
        Assert.Equal(5, answers[0].AnswerId);
        Assert.Equal(10, answers[1].RunId);
        Assert.Equal("GET /Assessments/1/Runs/10/Answers", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAssessmentRunAnsersReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Runs/10/Answers", HttpStatusCode.NotFound);

        Assert.Null(_service.GetAssessmentRunAnsers(1, 10));
    }

    [Fact]
    public void TestGetAssessmentRunAnsersReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Runs/10/Answers", HttpStatusCode.InternalServerError);

        Assert.Null(_service.GetAssessmentRunAnsers(1, 10));
    }

    // ---------------------------------------------------------------------------- Create

    [Fact]
    public void TestCreate()
    {
        _backend.OnPost("/Assessments", new Assessment { Id = 9, Name = "Vendor review", Created = FixedDate });

        var (code, assessment) = _service.Create(new Assessment { Name = "Vendor review", Created = FixedDate });

        Assert.Equal(0, code);
        Assert.NotNull(assessment);
        Assert.Equal(9, assessment.Id);
        Assert.Equal("POST /Assessments", _backend.LastRequest.ToString());
        Assert.Contains("Vendor review", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments", HttpStatusCode.NotFound);

        var (code, assessment) = _service.Create(new Assessment { Name = "Vendor review" });

        Assert.Equal(-1, code);
        Assert.Null(assessment);
    }

    [Fact]
    public void TestCreateReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Post, "/Assessments", HttpStatusCode.InternalServerError);

        var (code, assessment) = _service.Create(new Assessment { Name = "Vendor review" });

        Assert.Equal(-1, code);
        Assert.Null(assessment);
    }

    // ----------------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task TestCreateAsync()
    {
        _backend.OnPost("/Assessments", new Assessment { Id = 9, Name = "Yearly audit", Created = FixedDate });

        var (code, assessment) = await _service.CreateAsync(new Assessment { Name = "Yearly audit" });

        Assert.Equal(0, code);
        Assert.NotNull(assessment);
        Assert.Equal("Yearly audit", assessment.Name);
        Assert.Equal("POST /Assessments", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestCreateAsyncReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments", HttpStatusCode.NotFound);

        var (code, assessment) = await _service.CreateAsync(new Assessment { Name = "Yearly audit" });

        Assert.Equal(-1, code);
        Assert.Null(assessment);
    }

    [Fact]
    public async Task TestCreateAsyncReportsAnErrorOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Assessments");

        var (code, assessment) = await _service.CreateAsync(new Assessment { Name = "Yearly audit" });

        Assert.Equal(-1, code);
        Assert.Null(assessment);
    }

    // ----------------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task TestUpdateAsync()
    {
        _backend.OnStatus(Method.Put, "/Assessments/9", HttpStatusCode.OK);

        var result = await _service.UpdateAsync(new Assessment { Id = 9, Name = "Renamed" });

        Assert.Equal(0, result);
        Assert.Equal("PUT /Assessments/9", _backend.LastRequest.ToString());
        Assert.Contains("Renamed", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateAsyncReportsAnErrorOnANonOkStatus()
    {
        _backend.OnStatus(Method.Put, "/Assessments/9", HttpStatusCode.NotFound);

        Assert.Equal(1, await _service.UpdateAsync(new Assessment { Id = 9, Name = "Renamed" }));
    }

    [Fact]
    public async Task TestUpdateAsyncReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Put, "/Assessments/9", HttpStatusCode.InternalServerError);

        Assert.Equal(1, await _service.UpdateAsync(new Assessment { Id = 9, Name = "Renamed" }));
    }

    [Fact]
    public async Task TestUpdateAsyncReportsAnErrorOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Assessments/9");

        Assert.Equal(1, await _service.UpdateAsync(new Assessment { Id = 9, Name = "Renamed" }));
    }

    // ------------------------------------------------------------- CreateAssessmentRun

    [Fact]
    public void TestCreateAssessmentRun()
    {
        _backend.OnPost("/Assessments/1/Runs", new AssessmentRun
        {
            Id = 10, AssessmentId = 1, EntityId = 3, AnalystId = 4, ProgressPercentage = 0,
            Comments = "started", RunDate = FixedDate
        });

        var run = _service.CreateAssessmentRun(new AssessmentRunDto
        {
            AssessmentId = 1, EntityId = 3, AnalystId = 4, Comments = "started"
        });

        Assert.NotNull(run);
        Assert.Equal(10, run.Id);
        Assert.Equal(1, run.AssessmentId);
        Assert.Equal(3, run.EntityId);
        Assert.Equal("started", run.Comments);
        Assert.Equal("POST /Assessments/1/Runs", _backend.LastRequest.ToString());
    }

    [Theory]
    [InlineData(0, 3, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 3, 0)]
    public void TestCreateAssessmentRunRejectsUnsetIds(int assessmentId, int entityId, int analystId)
    {
        var ex = Assert.Throws<InvalidParameterException>(() => _service.CreateAssessmentRun(
            new AssessmentRunDto { AssessmentId = assessmentId, EntityId = entityId, AnalystId = analystId }));

        Assert.Equal("assessmentRun", ex.ParameterName);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestCreateAssessmentRunThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Runs", HttpStatusCode.NotFound);

        Assert.Throws<HttpRequestException>(() => _service.CreateAssessmentRun(
            new AssessmentRunDto { AssessmentId = 1, EntityId = 3, AnalystId = 4 }));
    }

    [Fact]
    public void TestCreateAssessmentRunPropagatesAServerError()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Runs", HttpStatusCode.InternalServerError);

        // The method rethrows whatever RestSharp raised, so only the fact that it does not swallow
        // the failure is asserted here.
        Assert.ThrowsAny<Exception>(() => _service.CreateAssessmentRun(
            new AssessmentRunDto { AssessmentId = 1, EntityId = 3, AnalystId = 4 }));
        Assert.True(_backend.Sent(Method.Post, "/Assessments/1/Runs"));
    }

    // ------------------------------------------------------------------- CreateRunAnswer

    [Fact]
    public void TestCreateRunAnswer()
    {
        _backend.OnPost("/Assessments/1/Runs/10/Answers",
            new AssessmentRunsAnswer { Id = 77, AnswerId = 5, QuestionId = 2, RunId = 10 });

        var created = _service.CreateRunAnswer(1,
            new AssessmentRunsAnswerDto { AnswerId = 5, QuestionId = 2, RunId = 10 });

        Assert.Equal(77, created.Id);
        Assert.Equal(5, created.AnswerId);
        Assert.Equal("POST /Assessments/1/Runs/10/Answers", _backend.LastRequest.ToString());
    }

    [Theory]
    [InlineData(0, 2, 10)]
    [InlineData(5, 0, 10)]
    [InlineData(5, 2, 0)]
    public void TestCreateRunAnswerRejectsUnsetIds(int answerId, int questionId, int runId)
    {
        var ex = Assert.Throws<InvalidParameterException>(() => _service.CreateRunAnswer(1,
            new AssessmentRunsAnswerDto { AnswerId = answerId, QuestionId = questionId, RunId = runId }));

        Assert.Equal("AssessmentRunsAnswerDto", ex.ParameterName);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestCreateRunAnswerThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Runs/10/Answers", HttpStatusCode.NotFound);

        Assert.Throws<HttpRequestException>(() => _service.CreateRunAnswer(1,
            new AssessmentRunsAnswerDto { AnswerId = 5, QuestionId = 2, RunId = 10 }));
    }

    // --------------------------------------------------------------------- CreateAnswers

    [Fact]
    public void TestCreateAnswersDoesNothingForAnEmptyList()
    {
        var (code, answers) = _service.CreateAnswers(1, 2, []);

        Assert.Equal(0, code);
        Assert.NotNull(answers);
        Assert.Empty(answers);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestCreateAnswersRefusesAnswersThatAlreadyHaveAnId()
    {
        var (code, answers) = _service.CreateAnswers(1, 2, [Answer(5, "already saved")]);

        Assert.Equal(-1, code);
        Assert.Null(answers);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestCreateAnswers()
    {
        _backend.OnPost("/Assessments/1/Questions/2/Answers",
            new List<AssessmentAnswer> { Answer(5, "Yes"), Answer(6, "No") },
            HttpStatusCode.Created);

        var input = new List<AssessmentAnswer> { NewAnswer("Yes"), NewAnswer("No") };

        var (code, answers) = _service.CreateAnswers(1, 2, input);

        Assert.Equal(0, code);
        Assert.NotNull(answers);
        Assert.Equal(2, answers.Count);
        Assert.Equal("Yes", answers[0].Answer);
        Assert.Equal(5, answers[0].Id);
        // The method stamps the owning ids on the objects it was handed.
        Assert.All(input, a =>
        {
            Assert.Equal(1, a.AssessmentId);
            Assert.Equal(2, a.QuestionId);
        });
        Assert.Equal("POST /Assessments/1/Questions/2/Answers", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestCreateAnswersReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions/2/Answers", HttpStatusCode.NotFound);

        var (code, answers) = _service.CreateAnswers(1, 2, [NewAnswer("Yes")]);

        Assert.Equal(-1, code);
        Assert.Null(answers);
    }

    [Fact]
    public void TestCreateAnswersReportsAFailureOnAConflict()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions/2/Answers", HttpStatusCode.Conflict);

        var (code, answers) = _service.CreateAnswers(1, 2, [NewAnswer("Yes")]);

        // A 409 never yields the success code and never yields data; whether it arrives through the
        // dedicated Conflict branch or through the catch-all depends on RestSharp raising on the
        // status, so only the failure itself is asserted.
        Assert.NotEqual(0, code);
        Assert.Null(answers);
        Assert.True(_backend.Sent(Method.Post, "/Assessments/1/Questions/2/Answers"));
    }

    [Fact]
    public void TestCreateAnswersReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions/2/Answers", HttpStatusCode.InternalServerError);

        var (code, answers) = _service.CreateAnswers(1, 2, [NewAnswer("Yes")]);

        Assert.Equal(-1, code);
        Assert.Null(answers);
    }

    // --------------------------------------------------------------------- UpdateAnswers

    [Fact]
    public void TestUpdateAnswersDoesNothingForAnEmptyList()
    {
        var (code, answers) = _service.UpdateAnswers(1, 2, []);

        Assert.Equal(0, code);
        Assert.NotNull(answers);
        Assert.Empty(answers);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestUpdateAnswersRefusesAnswersWithoutAnId()
    {
        var (code, answers) = _service.UpdateAnswers(1, 2, [NewAnswer("not saved yet")]);

        Assert.Equal(-1, code);
        Assert.Null(answers);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestUpdateAnswers()
    {
        _backend.OnPut("/Assessments/1/Questions/2/Answers",
            new List<AssessmentAnswer> { Answer(5, "Yes, always") });

        var (code, answers) = _service.UpdateAnswers(1, 2, [Answer(5, "Yes, always")]);

        Assert.Equal(0, code);
        Assert.NotNull(answers);
        Assert.Equal("Yes, always", Assert.Single(answers).Answer);
        Assert.Equal("PUT /Assessments/1/Questions/2/Answers", _backend.LastRequest.ToString());
        Assert.Contains("Yes, always", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateAnswersReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Questions/2/Answers", HttpStatusCode.NotFound);

        var (code, answers) = _service.UpdateAnswers(1, 2, [Answer(5, "Yes")]);

        Assert.Equal(-1, code);
        Assert.Null(answers);
    }

    [Fact]
    public void TestUpdateAnswersReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Questions/2/Answers", HttpStatusCode.InternalServerError);

        var (code, answers) = _service.UpdateAnswers(1, 2, [Answer(5, "Yes")]);

        Assert.Equal(-1, code);
        Assert.Null(answers);
    }

    // --------------------------------------------------------------------- DeleteAnswers

    [Fact]
    public void TestDeleteAnswersDoesNothingForAnEmptyList()
    {
        Assert.Equal(0, _service.DeleteAnswers(1, 2, []));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestDeleteAnswersStopsAtTheFirstAnswerTheServerDidNotConfirm()
    {
        // The method only accepts the literal "Ok" as confirmation.
        _backend.OnDelete("/Assessments/1/Questions/2/Answers/5", "\"Error\"");

        var result = _service.DeleteAnswers(1, 2, [Answer(5, "Yes"), Answer(6, "No")]);

        Assert.Equal(-1, result);
        Assert.Equal("DELETE /Assessments/1/Questions/2/Answers/5", _backend.LastRequest.ToString());
        // It gave up before touching the second answer.
        Assert.Single(_backend.Requests);
    }

    // --------------------------------------------------------------------- CreateQuestion

    [Fact]
    public void TestCreateQuestion()
    {
        _backend.OnPost("/Assessments/1/Questions",
            new AssessmentQuestion { Id = 4, AssessmentId = 1, Question = "Do you encrypt backups?", Order = 1, PageNumber = 2 },
            HttpStatusCode.Created);

        var (code, question) = _service.CreateQuestion(1,
            new AssessmentQuestion { AssessmentId = 1, Question = "Do you encrypt backups?", Order = 1, PageNumber = 2 });

        Assert.Equal(0, code);
        Assert.NotNull(question);
        Assert.Equal(4, question.Id);
        Assert.Equal("Do you encrypt backups?", question.Question);
        Assert.Equal(2, question.PageNumber);
        Assert.Equal("POST /Assessments/1/Questions", _backend.LastRequest.ToString());
        Assert.Contains("Do you encrypt backups?", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateQuestionReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions", HttpStatusCode.NotFound);

        var (code, question) = _service.CreateQuestion(1, new AssessmentQuestion { Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    [Fact]
    public void TestCreateQuestionReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions", HttpStatusCode.InternalServerError);

        var (code, question) = _service.CreateQuestion(1, new AssessmentQuestion { Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    [Fact]
    public void TestCreateQuestionReportsAFailureOnAConflict()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions", HttpStatusCode.Conflict);

        var (code, question) = _service.CreateQuestion(1, new AssessmentQuestion { Question = "Q" });

        Assert.NotEqual(0, code);
        Assert.Null(question);
    }

    // ---------------------------------------------------------------- CreateQuestionAsync

    [Fact]
    public async Task TestCreateQuestionAsync()
    {
        _backend.OnPost("/Assessments/1/Questions",
            new AssessmentQuestion { Id = 4, AssessmentId = 1, Question = "Is MFA enforced?", Order = 2 },
            HttpStatusCode.Created);

        var (code, question) = await _service.CreateQuestionAsync(1,
            new AssessmentQuestion { AssessmentId = 1, Question = "Is MFA enforced?", Order = 2 });

        Assert.Equal(0, code);
        Assert.NotNull(question);
        Assert.Equal("Is MFA enforced?", question.Question);
        Assert.Equal("POST /Assessments/1/Questions", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestCreateQuestionAsyncReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Assessments/1/Questions", HttpStatusCode.NotFound);

        var (code, question) = await _service.CreateQuestionAsync(1, new AssessmentQuestion { Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    [Fact]
    public async Task TestCreateQuestionAsyncReportsAnErrorOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Assessments/1/Questions");

        var (code, question) = await _service.CreateQuestionAsync(1, new AssessmentQuestion { Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    // --------------------------------------------------------------------- UpdateQuestion

    [Fact]
    public void TestUpdateQuestion()
    {
        _backend.OnPut("/Assessments/1/Questions",
            new AssessmentQuestion { Id = 4, AssessmentId = 1, Question = "Reworded?", Order = 1, ExplanationMarkdown = "**why**" });

        var (code, question) = _service.UpdateQuestion(1,
            new AssessmentQuestionDto { Id = 4, AssessmentId = 1, Question = "Reworded?", Order = 1 });

        Assert.Equal(0, code);
        Assert.NotNull(question);
        Assert.Equal("Reworded?", question.Question);
        Assert.Equal("**why**", question.ExplanationMarkdown);
        Assert.Equal("PUT /Assessments/1/Questions", _backend.LastRequest.ToString());
        Assert.Contains("Reworded?", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateQuestionReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Questions", HttpStatusCode.NotFound);

        var (code, question) = _service.UpdateQuestion(1, new AssessmentQuestionDto { Id = 4, Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    [Fact]
    public void TestUpdateQuestionReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Questions", HttpStatusCode.InternalServerError);

        var (code, question) = _service.UpdateQuestion(1, new AssessmentQuestionDto { Id = 4, Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    // ---------------------------------------------------------------- UpdateQuestionAsync

    [Fact]
    public async Task TestUpdateQuestionAsync()
    {
        _backend.OnPut("/Assessments/1/Questions",
            new AssessmentQuestion { Id = 4, AssessmentId = 1, Question = "Reworded again?", Order = 1 });

        var (code, question) = await _service.UpdateQuestionAsync(1,
            new AssessmentQuestionDto { Id = 4, AssessmentId = 1, Question = "Reworded again?", Order = 1 });

        Assert.Equal(0, code);
        Assert.NotNull(question);
        Assert.Equal("Reworded again?", question.Question);
        Assert.Equal("PUT /Assessments/1/Questions", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestUpdateQuestionAsyncReportsAnErrorWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Put, "/Assessments/1/Questions", HttpStatusCode.NotFound);

        var (code, question) = await _service.UpdateQuestionAsync(1,
            new AssessmentQuestionDto { Id = 4, Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    [Fact]
    public async Task TestUpdateQuestionAsyncReportsAnErrorOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Assessments/1/Questions");

        var (code, question) = await _service.UpdateQuestionAsync(1,
            new AssessmentQuestionDto { Id = 4, Question = "Q" });

        Assert.Equal(-1, code);
        Assert.Null(question);
    }

    // --------------------------------------------------------------------- DeleteQuestion

    [Fact]
    public void TestDeleteQuestion()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Questions/4", HttpStatusCode.OK);

        Assert.Equal(0, _service.DeleteQuestion(1, 4));
        Assert.Equal("DELETE /Assessments/1/Questions/4", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteQuestionReportsAnErrorOnANonOkStatus()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Questions/4", HttpStatusCode.NotFound);

        Assert.Equal(-1, _service.DeleteQuestion(1, 4));
    }

    [Fact]
    public void TestDeleteQuestionReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1/Questions/4", HttpStatusCode.InternalServerError);

        Assert.Equal(-1, _service.DeleteQuestion(1, 4));
    }

    // ---------------------------------------------------------------------------- Delete

    [Fact]
    public void TestDelete()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1", HttpStatusCode.OK);

        Assert.Equal(0, _service.Delete(1));
        Assert.Equal("DELETE /Assessments/1", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteReportsAnErrorOnANonOkStatus()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/1", HttpStatusCode.NotFound);

        Assert.Equal(-1, _service.Delete(1));
    }

    [Fact]
    public void TestDeleteReportsAnErrorOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/Assessments/1");

        Assert.Equal(-1, _service.Delete(1));
    }

    // ----------------------------------------------------------- GetAssessmentQuestions

    [Fact]
    public void TestGetAssessmentQuestions()
    {
        _backend.OnGet("/Assessments/1/Questions", new List<AssessmentQuestion>
        {
            new() { Id = 4, AssessmentId = 1, Question = "Do you encrypt backups?", Order = 1, PageNumber = 1 },
            new()
            {
                Id = 5, AssessmentId = 1, Question = "Which cipher?", Order = 2, PageNumber = 2,
                ParentQuestionId = 4, ConditionJson = "{\"op\":\"eq\"}"
            }
        });

        var questions = _service.GetAssessmentQuestions(1);

        Assert.NotNull(questions);
        Assert.Equal(2, questions.Count);
        Assert.Equal("Do you encrypt backups?", questions[0].Question);
        Assert.Equal(4, questions[1].ParentQuestionId);
        Assert.Equal("{\"op\":\"eq\"}", questions[1].ConditionJson);
        Assert.Equal("GET /Assessments/1/Questions", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAssessmentQuestionsReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Questions", HttpStatusCode.NotFound);

        Assert.Null(_service.GetAssessmentQuestions(1));
    }

    [Fact]
    public void TestGetAssessmentQuestionsReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Questions", HttpStatusCode.InternalServerError);

        Assert.Null(_service.GetAssessmentQuestions(1));
    }

    // ------------------------------------------------------------- GetAssessmentAnswers

    [Fact]
    public void TestGetAssessmentAnswers()
    {
        _backend.OnGet("/Assessments/1/Answers", new List<AssessmentAnswer> { Answer(5, "Yes"), Answer(6, "No") });

        var answers = _service.GetAssessmentAnswers(1);

        Assert.NotNull(answers);
        Assert.Equal(2, answers.Count);
        Assert.Equal("Yes", answers[0].Answer);
        Assert.Equal(2, answers[1].QuestionId);
        Assert.Equal("GET /Assessments/1/Answers", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAssessmentAnswersReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Answers", HttpStatusCode.NotFound);

        Assert.Null(_service.GetAssessmentAnswers(1));
    }

    [Fact]
    public void TestGetAssessmentAnswersReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Get, "/Assessments/1/Answers", HttpStatusCode.InternalServerError);

        Assert.Null(_service.GetAssessmentAnswers(1));
    }

    // -------------------------------------------------- GetVisibleQuestionsForPageAsync

    [Fact]
    public async Task TestGetVisibleQuestionsForPageAsync()
    {
        _backend.OnGet("/Assessments/runs/10/pages/2/questions", new List<AssessmentQuestion>
        {
            new() { Id = 5, AssessmentId = 1, Question = "Which cipher?", Order = 2, PageNumber = 2 }
        });

        var questions = await _service.GetVisibleQuestionsForPageAsync(10, 2);

        Assert.NotNull(questions);
        Assert.Equal("Which cipher?", Assert.Single(questions).Question);
        Assert.Equal("GET /Assessments/runs/10/pages/2/questions", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetVisibleQuestionsForPageAsyncReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments/runs/10/pages/2/questions", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetVisibleQuestionsForPageAsync(10, 2));
    }

    [Fact]
    public async Task TestGetVisibleQuestionsForPageAsyncReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Get, "/Assessments/runs/10/pages/2/questions", HttpStatusCode.InternalServerError);

        Assert.Null(await _service.GetVisibleQuestionsForPageAsync(10, 2));
    }

    // ------------------------------------------------------------- GetDraftAnswersAsync

    [Fact]
    public async Task TestGetDraftAnswersAsync()
    {
        _backend.OnGet("/Assessments/runs/10/answers/draft", new List<AssessmentRunAnswer>
        {
            new()
            {
                Id = 1, AssessmentRunId = 10, AssessmentQuestionId = 4,
                AnswerContentJson = "{\"selected\":5}", LastUpdatedAt = FixedDate
            }
        });

        var drafts = await _service.GetDraftAnswersAsync(10);

        Assert.NotNull(drafts);
        var draft = Assert.Single(drafts);
        Assert.Equal(10, draft.AssessmentRunId);
        Assert.Equal(4, draft.AssessmentQuestionId);
        Assert.Equal("{\"selected\":5}", draft.AnswerContentJson);
        Assert.Equal("GET /Assessments/runs/10/answers/draft", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetDraftAnswersAsyncReturnsNullWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Assessments/runs/10/answers/draft", HttpStatusCode.NotFound);

        Assert.Null(await _service.GetDraftAnswersAsync(10));
    }

    [Fact]
    public async Task TestGetDraftAnswersAsyncReturnsNullOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Assessments/runs/10/answers/draft");

        Assert.Null(await _service.GetDraftAnswersAsync(10));
    }

    // ------------------------------------------------------------ SaveDraftAnswerAsync

    [Fact]
    public async Task TestSaveDraftAnswerAsync()
    {
        _backend.On(Method.Patch, "/Assessments/runs/10/answers", new AssessmentRunAnswer
        {
            Id = 1, AssessmentRunId = 10, AssessmentQuestionId = 4,
            AnswerContentJson = "selected-5", LastUpdatedAt = FixedDate
        });

        var saved = await _service.SaveDraftAnswerAsync(10, 4, "selected-5");

        Assert.NotNull(saved);
        Assert.Equal(1, saved.Id);
        Assert.Equal(4, saved.AssessmentQuestionId);
        Assert.Equal(FixedDate, saved.LastUpdatedAt);
        Assert.Equal("PATCH /Assessments/runs/10/answers", _backend.LastRequest.ToString());
        Assert.Contains("selected-5", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestSaveDraftAnswerAsyncReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Patch, "/Assessments/runs/10/answers", HttpStatusCode.InternalServerError);

        Assert.Null(await _service.SaveDraftAnswerAsync(10, 4, "selected-5"));
    }

    [Fact]
    public async Task TestSaveDraftAnswerAsyncReturnsNullOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Patch, "/Assessments/runs/10/answers");

        Assert.Null(await _service.SaveDraftAnswerAsync(10, 4, "selected-5"));
    }

    // ------------------------------------------------------------- PreviewTemplateAsync

    [Fact]
    public async Task TestPreviewTemplateAsyncUploadsTheFileAndTheNameOverride()
    {
        _backend.OnPost("/Imports/assessment/preview", new AssessmentImportPreview
        {
            Valid = true, Name = "Imported", Description = "from excel",
            PageCount = 2, QuestionCount = 5, AnswerCount = 10,
            Warnings = ["duplicate question text"]
        });

        var path = WriteTempTemplate();
        try
        {
            var preview = await _service.PreviewTemplateAsync(path, "Imported");

            Assert.NotNull(preview);
            Assert.True(preview.Valid);
            Assert.Equal("Imported", preview.Name);
            Assert.Equal(2, preview.PageCount);
            Assert.Equal(5, preview.QuestionCount);
            Assert.Equal(10, preview.AnswerCount);
            Assert.Equal("duplicate question text", Assert.Single(preview.Warnings));
            Assert.Equal("/Imports/assessment/preview", _backend.LastRequest.Path);
            Assert.Equal("?assessmentName=Imported", _backend.LastRequest.Query);
            Assert.Contains("pages", _backend.LastRequest.Body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TestPreviewTemplateAsyncOmitsTheNameWhenItWasNotGiven()
    {
        _backend.OnPost("/Imports/assessment/preview", new AssessmentImportPreview
        {
            Valid = false,
            Errors = [new AssessmentImportError { Row = 3, Message = "missing answer column" }]
        });

        var path = WriteTempTemplate();
        try
        {
            var preview = await _service.PreviewTemplateAsync(path, null);

            Assert.NotNull(preview);
            Assert.False(preview.Valid);
            var error = Assert.Single(preview.Errors);
            Assert.Equal(3, error.Row);
            Assert.Equal("missing answer column", error.Message);
            Assert.Equal("", _backend.LastRequest.Query);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TestPreviewTemplateAsyncReturnsNullOnAServerError()
    {
        _backend.OnStatus(Method.Post, "/Imports/assessment/preview", HttpStatusCode.InternalServerError);

        var path = WriteTempTemplate();
        try
        {
            Assert.Null(await _service.PreviewTemplateAsync(path, "Imported"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // -------------------------------------------------------------- ImportTemplateAsync

    [Fact]
    public async Task TestImportTemplateAsync()
    {
        _backend.OnPost("/Imports/assessment", new Assessment { Id = 12, Name = "Imported", Created = FixedDate });

        var path = WriteTempTemplate();
        try
        {
            var (code, assessment) = await _service.ImportTemplateAsync(path, "Imported");

            Assert.Equal(0, code);
            Assert.NotNull(assessment);
            Assert.Equal(12, assessment.Id);
            Assert.Equal("Imported", assessment.Name);
            Assert.Equal("/Imports/assessment", _backend.LastRequest.Path);
            Assert.Equal("?assessmentName=Imported", _backend.LastRequest.Query);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TestImportTemplateAsyncReportsAnErrorOnAServerError()
    {
        _backend.OnStatus(Method.Post, "/Imports/assessment", HttpStatusCode.InternalServerError);

        var path = WriteTempTemplate();
        try
        {
            var (code, assessment) = await _service.ImportTemplateAsync(path, null);

            Assert.Equal(-1, code);
            Assert.Null(assessment);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TestImportTemplateAsyncReportsAnErrorOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Imports/assessment");

        var path = WriteTempTemplate();
        try
        {
            var (code, assessment) = await _service.ImportTemplateAsync(path, null);

            Assert.Equal(-1, code);
            Assert.Null(assessment);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
