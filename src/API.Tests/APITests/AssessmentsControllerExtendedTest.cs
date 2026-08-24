using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using DAL.EntitiesDto;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Covers the branches of <see cref="AssessmentsController"/> that
/// <see cref="AssessmentsControllerTest"/> leaves untouched: the not-found guards, the
/// conflict/bad-request guards, the failed-persistence guards, every catch-all 500 handler and the
/// draft-answer / paged-question actions.
/// </summary>
[TestSubject(typeof(AssessmentsController))]
public class AssessmentsControllerExtendedTest : BaseControllerTest
{
    private const int OkId = 1;
    private const int NoQuestionsId = 2;
    private const int DeleteFailsId = 3;
    private const int NotFoundId = 999;
    private const int ErrorId = 500;

    private readonly IAssessmentsService _assessmentsService = Substitute.For<IAssessmentsService>();
    private readonly AssessmentsController _controller;

    public AssessmentsControllerExtendedTest()
    {
        var assessment = new Assessment { Id = OkId, Name = "A1", Created = new DateTime(2026, 1, 1) };
        var questionOne = new AssessmentQuestion { Id = 1, AssessmentId = OkId, Question = "Q1", Order = 1 };
        var questionTwo = new AssessmentQuestion { Id = 2, AssessmentId = OkId, Question = "Q2", Order = 2 };
        var questionThree = new AssessmentQuestion { Id = 3, AssessmentId = OkId, Question = "Q3", Order = 3 };
        var answerOne = new AssessmentAnswer { Id = 1, AssessmentId = OkId, QuestionId = 1, Answer = "A1" };
        var answerTwo = new AssessmentAnswer { Id = 2, AssessmentId = OkId, QuestionId = 1, Answer = "A2" };
        var run = new AssessmentRun { Id = 1, AssessmentId = OkId, EntityId = 1, AnalystId = 1, HostId = 1 };

        // Assessment lookups
        _assessmentsService.List().Returns(new List<Assessment> { assessment });
        _assessmentsService.Get(OkId).Returns(assessment);
        _assessmentsService.Get(NoQuestionsId).Returns(new Assessment
            { Id = NoQuestionsId, Name = "A2", Created = new DateTime(2026, 1, 1) });
        _assessmentsService.Get(DeleteFailsId).Returns(new Assessment
            { Id = DeleteFailsId, Name = "A3", Created = new DateTime(2026, 1, 1) });
        _assessmentsService.Get(NotFoundId).Returns((Assessment)null);
        _assessmentsService.Get(ErrorId).Returns(_ => throw new Exception("boom"));

        // Runs
        _assessmentsService.GetRuns(OkId).Returns(new List<AssessmentRun> { run });
        _assessmentsService.GetRuns(NotFoundId).Returns((List<AssessmentRun>)null);
        _assessmentsService.GetRuns(ErrorId).Returns(_ => throw new Exception("boom"));
        _assessmentsService.GetRun(OkId).Returns(run);
        _assessmentsService.GetRun(NotFoundId).Returns((AssessmentRun)null);
        _assessmentsService.GetRun(ErrorId).Returns(_ => throw new Exception("boom"));
        _assessmentsService.CreateRun(Arg.Any<AssessmentRunDto>()).Returns(run);

        // Run answers
        _assessmentsService.GetRunsAnswers(OkId)
            .Returns(new List<AssessmentRunsAnswer> { new() { Id = 1, RunId = 1, QuestionId = 1, AnswerId = 1 } });
        _assessmentsService.GetRunsAnswers(ErrorId).Returns(_ => throw new Exception("boom"));
        _assessmentsService.CreateRunAnswer(Arg.Any<AssessmentRunsAnswer>())
            .Returns(ci => ci.Arg<AssessmentRunsAnswer>());
        _assessmentsService.CreateRunAnswer(Arg.Is<AssessmentRunsAnswer>(a => a.RunId == ErrorId))
            .Returns(_ => throw new Exception("boom"));
        _assessmentsService.When(x => x.DeleteRunAnswer(ErrorId, Arg.Any<int>(), Arg.Any<int>()))
            .Do(_ => throw new Exception("boom"));
        _assessmentsService.When(x => x.DeleteAllRunAnswer(ErrorId, Arg.Any<int>()))
            .Do(_ => throw new Exception("boom"));

        // Questions
        _assessmentsService.GetQuestions(OkId).Returns(new List<AssessmentQuestion> { questionOne, questionTwo });
        _assessmentsService.GetQuestions(NoQuestionsId).Returns((List<AssessmentQuestion>)null);
        _assessmentsService.GetQuestion(OkId, "dup").Returns(questionOne);
        _assessmentsService.GetQuestion(OkId, "new").Returns((AssessmentQuestion)null);
        _assessmentsService.GetQuestion(OkId, "savefail").Returns((AssessmentQuestion)null);
        _assessmentsService.GetQuestionById(1).Returns(questionOne);
        _assessmentsService.GetQuestionById(3).Returns(questionThree);
        _assessmentsService.GetQuestionById(NotFoundId).Returns((AssessmentQuestion)null);
        _assessmentsService.GetQuestionById(OkId, 1).Returns(questionOne);
        _assessmentsService.GetQuestionById(OkId, 2).Returns(questionTwo);
        _assessmentsService.GetQuestionById(OkId, NotFoundId).Returns((AssessmentQuestion)null);
        _assessmentsService.SaveQuestion(Arg.Any<AssessmentQuestion>()).Returns(ci => ci.Arg<AssessmentQuestion>());
        _assessmentsService.SaveQuestion(Arg.Is<AssessmentQuestion>(q => q.Question == "savefail"))
            .Returns((AssessmentQuestion)null);

        // Answers
        _assessmentsService.GetAnswers(OkId).Returns(new List<AssessmentAnswer> { answerOne, answerTwo });
        _assessmentsService.GetAnswers(ErrorId).Returns(_ => throw new Exception("boom"));
        _assessmentsService.GetAnswer(OkId, 1, "dup").Returns(answerOne);
        _assessmentsService.GetAnswer(OkId, 1, "new").Returns((AssessmentAnswer)null);
        _assessmentsService.GetAnswerById(1).Returns(answerOne);
        _assessmentsService.GetAnswerById(2).Returns(answerTwo);
        _assessmentsService.GetAnswerById(NotFoundId).Returns((AssessmentAnswer)null);
        _assessmentsService.SaveAnswer(Arg.Any<AssessmentAnswer>()).Returns(ci => ci.Arg<AssessmentAnswer>());
        _assessmentsService.DeleteAnswer(Arg.Any<AssessmentAnswer>()).Returns(0);
        _assessmentsService.DeleteAnswer(Arg.Is<AssessmentAnswer>(a => a.Id == 2)).Returns(-1);
        _assessmentsService.DeleteQuestion(Arg.Any<AssessmentQuestion>()).Returns(0);
        _assessmentsService.DeleteQuestion(Arg.Is<AssessmentQuestion>(q => q.Id == 2)).Returns(-1);

        // Create / update / delete assessment
        _assessmentsService.Create(Arg.Is<Assessment>(a => a.Name == "created"))
            .Returns(_ => new Tuple<int, Assessment>(0, new Assessment
                { Id = 7, Name = "created", Created = new DateTime(2026, 1, 1) }));
        _assessmentsService.Create(Arg.Is<Assessment>(a => a.Name == "duplicated"))
            .Returns(_ => new Tuple<int, Assessment>(1, new Assessment
                { Id = 8, Name = "duplicated", Created = new DateTime(2026, 1, 1) }));
        _assessmentsService.Create(Arg.Is<Assessment>(a => a.Name == "no-entity"))
            .Returns(_ => new Tuple<int, Assessment>(0, null));
        _assessmentsService.Create(Arg.Is<Assessment>(a => a.Name == "unknown-code"))
            .Returns(_ => new Tuple<int, Assessment>(-1, new Assessment
                { Id = 9, Name = "unknown-code", Created = new DateTime(2026, 1, 1) }));
        _assessmentsService.Create(Arg.Is<Assessment>(a => a.Name == "boom"))
            .Returns(_ => throw new Exception("boom"));

        _assessmentsService.When(x => x.Update(Arg.Is<Assessment>(a => a.Name == "boom")))
            .Do(_ => throw new Exception("boom"));

        _assessmentsService.Delete(Arg.Any<Assessment>()).Returns(0);
        _assessmentsService.Delete(Arg.Is<Assessment>(a => a.Id == DeleteFailsId)).Returns(-1);

        // Draft answers and paged questions
        _assessmentsService.SaveDraftAnswerAsync(OkId, Arg.Any<int>(), Arg.Any<string>())
            .Returns(new AssessmentRunAnswer
            {
                Id = 1,
                AssessmentRunId = OkId,
                AssessmentQuestionId = 1,
                AnswerContentJson = "{\"value\":true}"
            });
        _assessmentsService.SaveDraftAnswerAsync(ErrorId, Arg.Any<int>(), Arg.Any<string>())
            .Returns<Task<AssessmentRunAnswer>>(_ => throw new Exception("boom"));

        _assessmentsService.GetDraftAnswersAsync(OkId).Returns(new List<AssessmentRunAnswer>
        {
            new() { Id = 1, AssessmentRunId = OkId, AssessmentQuestionId = 1, AnswerContentJson = "{}" }
        });
        _assessmentsService.GetDraftAnswersAsync(ErrorId)
            .Returns<Task<List<AssessmentRunAnswer>>>(_ => throw new Exception("boom"));

        _assessmentsService.GetVisibleQuestionsForPageAsync(OkId, 1)
            .Returns(new List<AssessmentQuestion> { questionOne });
        _assessmentsService.GetVisibleQuestionsForPageAsync(ErrorId, Arg.Any<int>())
            .Returns<Task<List<AssessmentQuestion>>>(_ => throw new Exception("boom"));

        _controller = Build(_assessmentsService);
    }

    private static AssessmentsController Build(IAssessmentsService assessmentsService)
    {
        return ResolveController<AssessmentsController>(s => s.AddSingleton(assessmentsService));
    }

    private static void AssertServerError(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode.GetValueOrDefault());
    }

    // ---------------- GetAll ----------------

    [Fact]
    public void TestGetAllReturnsServerErrorOnException()
    {
        var service = Substitute.For<IAssessmentsService>();
        service.List().Returns(_ => throw new Exception("boom"));

        AssertServerError(Build(service).GetAll().Result);
    }

    // ---------------- GetAssessment ----------------

    [Fact]
    public void TestGetAssessmentReturnsServerErrorOnException()
    {
        AssertServerError(_controller.GetAssessment(ErrorId).Result);
    }

    // ---------------- GetAssessmentRuns ----------------

    [Fact]
    public void TestGetAssessmentRunsReturnsServerErrorOnException()
    {
        AssertServerError(_controller.GetAssessmentRuns(ErrorId).Result);
    }

    // ---------------- CreateAssessmentRun ----------------

    [Fact]
    public void TestCreateAssessmentRunNotFound()
    {
        var result = _controller.CreateAssessmentRun(NotFoundId, new AssessmentRunDto { AssessmentId = NotFoundId });
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestCreateAssessmentRunReturnsServerErrorOnException()
    {
        var result = _controller.CreateAssessmentRun(ErrorId, new AssessmentRunDto { AssessmentId = ErrorId });
        AssertServerError(result.Result);
    }

    // ---------------- UpdateAssessmentRun ----------------

    [Fact]
    public void TestUpdateAssessmentRunSetsTheAnalystToTheCurrentUser()
    {
        var dto = new AssessmentRunDto { Id = 1, AssessmentId = OkId, EntityId = 1, HostId = 1 };

        Assert.IsType<OkObjectResult>(_controller.UpdateAssessmentRun(OkId, 1, dto).Result);
        Assert.Equal(1, dto.AnalystId.GetValueOrDefault());
        _assessmentsService.Received(1).UpdateRun(dto);
    }

    [Fact]
    public void TestUpdateAssessmentRunAssessmentNotFound()
    {
        var result = _controller.UpdateAssessmentRun(NotFoundId, 1, new AssessmentRunDto { Id = 1 });
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Assessment not found", notFound.Value);
    }

    [Fact]
    public void TestUpdateAssessmentRunRunNotFound()
    {
        var result = _controller.UpdateAssessmentRun(OkId, NotFoundId, new AssessmentRunDto { Id = NotFoundId });
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Assessment run not found", notFound.Value);
    }

    [Fact]
    public void TestUpdateAssessmentRunReturnsServerErrorOnException()
    {
        var result = _controller.UpdateAssessmentRun(ErrorId, 1, new AssessmentRunDto { Id = 1 });
        AssertServerError(result.Result);
    }

    // ---------------- DeleteAssessmentRun ----------------

    [Fact]
    public void TestDeleteAssessmentRunNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.DeleteAssessmentRun(OkId, NotFoundId));
    }

    [Fact]
    public void TestDeleteAssessmentRunReturnsServerErrorOnException()
    {
        AssertServerError(_controller.DeleteAssessmentRun(OkId, ErrorId));
    }

    // ---------------- GetAssessmentRunsQuestions ----------------

    [Fact]
    public void TestGetAssessmentRunsQuestionsReturnsServerErrorOnException()
    {
        AssertServerError(_controller.GetAssessmentRunsQuestions(OkId, ErrorId).Result);
    }

    // ---------------- DeleteAllRunAnswers ----------------

    [Fact]
    public void TestDeleteAllRunAnswersNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.DeleteAllRunAnswers(OkId, NotFoundId).Result);
    }

    [Fact]
    public void TestDeleteAllRunAnswersReturnsServerErrorOnException()
    {
        AssertServerError(_controller.DeleteAllRunAnswers(ErrorId, OkId).Result);
    }

    // ---------------- CreateAssessmentRunsQuestion ----------------

    [Fact]
    public void TestCreateAssessmentRunsQuestionReturnsServerErrorOnException()
    {
        var result = _controller.CreateAssessmentRunsQuestion(OkId, ErrorId, new AssessmentRunsAnswerDto
        {
            RunId = ErrorId, QuestionId = 1, AnswerId = 1
        });
        AssertServerError(result.Result);
    }

    [Fact]
    public void TestDeleteAssessmentRunsQuestionReturnsServerErrorOnException()
    {
        AssertServerError(_controller.CreateAssessmentRunsQuestion(ErrorId, 1, 1).Result);
    }

    // ---------------- DeleteAssessment ----------------

    [Fact]
    public void TestDeleteAssessmentNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(_controller.DeleteAssessment(NotFoundId));
    }

    [Fact]
    public void TestDeleteAssessmentReturnsServerErrorWhenTheServiceFails()
    {
        AssertServerError(_controller.DeleteAssessment(DeleteFailsId));
    }

    [Fact]
    public void TestDeleteAssessmentReturnsServerErrorOnException()
    {
        AssertServerError(_controller.DeleteAssessment(ErrorId));
    }

    // ---------------- CreateAssessment ----------------

    [Fact]
    public void TestCreateAssessmentConflictWhenItAlreadyExists()
    {
        var result = _controller.CreateAssessment(new Assessment { Name = "duplicated" });
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public void TestCreateAssessmentReturnsServerErrorWhenNoEntityIsReturned()
    {
        AssertServerError(_controller.CreateAssessment(new Assessment { Name = "no-entity" }).Result);
    }

    [Fact]
    public void TestCreateAssessmentReturnsServerErrorOnUnknownResultCode()
    {
        AssertServerError(_controller.CreateAssessment(new Assessment { Name = "unknown-code" }).Result);
    }

    [Fact]
    public void TestCreateAssessmentReturnsServerErrorOnException()
    {
        AssertServerError(_controller.CreateAssessment(new Assessment { Name = "boom" }).Result);
    }

    // ---------------- UpdateAssessment ----------------

    [Fact]
    public void TestUpdateAssessmentOverwritesTheIdFromTheRoute()
    {
        var assessment = new Assessment { Id = 42, Name = "renamed" };

        Assert.IsType<OkObjectResult>(_controller.UpdateAssessment(OkId, assessment).Result);
        Assert.Equal(OkId, assessment.Id);
        _assessmentsService.Received(1).Update(assessment);
    }

    [Fact]
    public void TestUpdateAssessmentReturnsServerErrorOnException()
    {
        AssertServerError(_controller.UpdateAssessment(OkId, new Assessment { Name = "boom" }).Result);
    }

    // ---------------- ListAssessmentAnswers ----------------

    [Fact]
    public void TestListAssessmentAnswersReturnsServerErrorOnException()
    {
        AssertServerError(_controller.ListAssessmentAnswers(ErrorId).Result);
    }

    // ---------------- ListAssessmentQuestions ----------------

    [Fact]
    public void TestListAssessmentQuestionsWithoutQuestions()
    {
        var result = _controller.ListAssessmentQuestions(NoQuestionsId);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Questions not found", notFound.Value);
    }

    [Fact]
    public void TestListAssessmentQuestionsReturnsServerErrorOnException()
    {
        AssertServerError(_controller.ListAssessmentQuestions(ErrorId).Result);
    }

    // ---------------- CreateAssessmentQuestion ----------------

    [Fact]
    public void TestCreateAssessmentQuestionAssessmentNotFound()
    {
        var result = _controller.CreateAssessmentQuestion(NotFoundId, new AssessmentQuestionDto
        {
            Id = 0, AssessmentId = NotFoundId, Question = "new", Order = 1
        });
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestCreateAssessmentQuestionWithoutTextIsRejected()
    {
        var result = _controller.CreateAssessmentQuestion(OkId, new AssessmentQuestionDto
        {
            Id = 0, AssessmentId = OkId, Question = null, Order = 1
        });
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Question cannot be null", badRequest.Value);
    }

    [Fact]
    public void TestCreateAssessmentQuestionResetsANonZeroId()
    {
        var dto = new AssessmentQuestionDto { Id = 77, AssessmentId = OkId, Question = "new", Order = 1 };

        var result = _controller.CreateAssessmentQuestion(OkId, dto);

        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(0, dto.Id);
    }

    [Fact]
    public void TestCreateAssessmentQuestionReturnsServerErrorWhenSaveFails()
    {
        var result = _controller.CreateAssessmentQuestion(OkId, new AssessmentQuestionDto
        {
            Id = 0, AssessmentId = OkId, Question = "savefail", Order = 1
        });
        AssertServerError(result.Result);
    }

    [Fact]
    public void TestCreateAssessmentQuestionReturnsServerErrorOnException()
    {
        var result = _controller.CreateAssessmentQuestion(ErrorId, new AssessmentQuestionDto
        {
            Id = 0, AssessmentId = ErrorId, Question = "new", Order = 1
        });
        AssertServerError(result.Result);
    }

    // ---------------- UpdateAssessmentQuestion ----------------

    [Fact]
    public void TestUpdateAssessmentQuestionAssessmentNotFound()
    {
        var result = _controller.UpdateAssessmentQuestion(NotFoundId, new AssessmentQuestionDto
        {
            Id = 1, AssessmentId = NotFoundId, Question = "Q1", Order = 1
        });
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestUpdateAssessmentQuestionOfAnotherAssessmentIsRejected()
    {
        var result = _controller.UpdateAssessmentQuestion(OkId, new AssessmentQuestionDto
        {
            Id = 1, AssessmentId = 77, Question = "Q1", Order = 1
        });
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Trying to update wrong question", conflict.Value);
    }

    [Fact]
    public void TestUpdateAssessmentQuestionReturnsServerErrorWhenSaveFails()
    {
        var result = _controller.UpdateAssessmentQuestion(OkId, new AssessmentQuestionDto
        {
            Id = 3, AssessmentId = OkId, Question = "savefail", Order = 3
        });
        AssertServerError(result.Result);
    }

    [Fact]
    public void TestUpdateAssessmentQuestionReturnsServerErrorOnException()
    {
        var result = _controller.UpdateAssessmentQuestion(ErrorId, new AssessmentQuestionDto
        {
            Id = 1, AssessmentId = ErrorId, Question = "Q1", Order = 1
        });
        AssertServerError(result.Result);
    }

    // ---------------- CreateAssessmentAnswers ----------------

    [Fact]
    public void TestCreateAssessmentAnswersAssessmentNotFound()
    {
        var result = _controller.CreateAssessmentAnswers(NotFoundId, 1, new AssessmentAnswerDto[0]);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Assessment not found", notFound.Value);
    }

    [Fact]
    public void TestCreateAssessmentAnswersQuestionNotFound()
    {
        var result = _controller.CreateAssessmentAnswers(OkId, NotFoundId, new AssessmentAnswerDto[0]);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Question not found", notFound.Value);
    }

    [Fact]
    public void TestCreateAssessmentAnswersRejectsAnswersOfAnotherQuestion()
    {
        var result = _controller.CreateAssessmentAnswers(OkId, 1, new[]
        {
            new AssessmentAnswerDto { Id = 0, AssessmentId = OkId, QuestionId = 77, Answer = "new" }
        });
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Trying to save inconsistent answer", conflict.Value);
    }

    [Fact]
    public void TestCreateAssessmentAnswersReturnsServerErrorOnException()
    {
        var result = _controller.CreateAssessmentAnswers(ErrorId, 1, new AssessmentAnswerDto[0]);
        AssertServerError(result.Result);
    }

    // ---------------- UpdateAssessmentAnswers ----------------

    [Fact]
    public void TestUpdateAssessmentAnswersAssessmentNotFound()
    {
        var result = _controller.UpdateAssessmentAnswers(NotFoundId, 1, new AssessmentAnswerDto[0]);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Assessment not found", notFound.Value);
    }

    [Fact]
    public void TestUpdateAssessmentAnswersQuestionNotFound()
    {
        var result = _controller.UpdateAssessmentAnswers(OkId, NotFoundId, new AssessmentAnswerDto[0]);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Question not found", notFound.Value);
    }

    [Fact]
    public void TestUpdateAssessmentAnswersRejectsAnswersOfAnotherQuestion()
    {
        var result = _controller.UpdateAssessmentAnswers(OkId, 1, new[]
        {
            new AssessmentAnswerDto { Id = 1, AssessmentId = 77, QuestionId = 1, Answer = "A1" }
        });
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public void TestUpdateAssessmentAnswersReturnsServerErrorOnException()
    {
        var result = _controller.UpdateAssessmentAnswers(ErrorId, 1, new AssessmentAnswerDto[0]);
        AssertServerError(result.Result);
    }

    // ---------------- DeleteAssessmentAnswer ----------------

    [Fact]
    public void TestDeleteAssessmentAnswerAssessmentNotFound()
    {
        var result = _controller.DeleteAssessmentAnswer(NotFoundId, 1, 1);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Assessment not found", notFound.Value);
    }

    [Fact]
    public void TestDeleteAssessmentAnswerQuestionNotFound()
    {
        var result = _controller.DeleteAssessmentAnswer(OkId, NotFoundId, 1);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Question not found", notFound.Value);
    }

    [Fact]
    public void TestDeleteAssessmentAnswerAnswerNotFound()
    {
        var result = _controller.DeleteAssessmentAnswer(OkId, 1, NotFoundId);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Answer not found", notFound.Value);
    }

    [Fact]
    public void TestDeleteAssessmentAnswerReportsAFailedDeletion()
    {
        var result = _controller.DeleteAssessmentAnswer(OkId, 1, 2);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Error", ok.Value);
    }

    [Fact]
    public void TestDeleteAssessmentAnswerReturnsServerErrorOnException()
    {
        AssertServerError(_controller.DeleteAssessmentAnswer(ErrorId, 1, 1).Result);
    }

    // ---------------- DeleteAssessmentQuestion ----------------

    [Fact]
    public void TestDeleteAssessmentQuestionAssessmentNotFound()
    {
        var result = _controller.DeleteAssessmentQuestion(NotFoundId, 1);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Assessment not found", notFound.Value);
    }

    [Fact]
    public void TestDeleteAssessmentQuestionQuestionNotFound()
    {
        var result = _controller.DeleteAssessmentQuestion(OkId, NotFoundId);
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Question not found", notFound.Value);
    }

    [Fact]
    public void TestDeleteAssessmentQuestionReportsAFailedDeletion()
    {
        var result = _controller.DeleteAssessmentQuestion(OkId, 2);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Error", ok.Value);
    }

    [Fact]
    public void TestDeleteAssessmentQuestionReturnsServerErrorOnException()
    {
        AssertServerError(_controller.DeleteAssessmentQuestion(ErrorId, 1).Result);
    }

    // ---------------- SaveDraftAnswer ----------------

    [Fact]
    public async Task TestSaveDraftAnswer()
    {
        var result = await _controller.SaveDraftAnswer(OkId, new SaveDraftAnswerRequest
        {
            QuestionId = 1, AnswerContentJson = "{\"value\":true}"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var answer = Assert.IsType<AssessmentRunAnswer>(ok.Value);
        Assert.Equal("{\"value\":true}", answer.AnswerContentJson);
    }

    [Fact]
    public async Task TestSaveDraftAnswerReturnsServerErrorOnException()
    {
        var result = await _controller.SaveDraftAnswer(ErrorId, new SaveDraftAnswerRequest
        {
            QuestionId = 1, AnswerContentJson = "{}"
        });
        AssertServerError(result.Result);
    }

    // ---------------- GetDraftAnswers ----------------

    [Fact]
    public async Task TestGetDraftAnswers()
    {
        var result = await _controller.GetDraftAnswers(OkId);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<List<AssessmentRunAnswer>>(ok.Value));
    }

    [Fact]
    public async Task TestGetDraftAnswersReturnsServerErrorOnException()
    {
        AssertServerError((await _controller.GetDraftAnswers(ErrorId)).Result);
    }

    // ---------------- GetVisibleQuestionsForPage ----------------

    [Fact]
    public async Task TestGetVisibleQuestionsForPage()
    {
        var result = await _controller.GetVisibleQuestionsForPage(OkId, 1);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<List<AssessmentQuestion>>(ok.Value));
    }

    [Fact]
    public async Task TestGetVisibleQuestionsForPageReturnsServerErrorOnException()
    {
        AssertServerError((await _controller.GetVisibleQuestionsForPage(ErrorId, 1)).Result);
    }
}
