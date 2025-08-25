using System;
using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(AssessmentsController))]
public class AssessmentsControllerTest: BaseControllerTest
{
    private readonly AssessmentsController _controller;

    public AssessmentsControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<AssessmentsController>();
    }

    [Fact]
    public void TestGetAll()
    {
        var result = _controller.GetAll();
        Assert.NotNull(result);
        // Success path returns value directly (Result is null)
        Assert.Null(result.Result);
        var list = Assert.IsType<List<Assessment>>(result.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void TestGetAssessment()
    {
        var okResult = _controller.GetAssessment(1);
        Assert.Null(okResult.Result);
        Assert.IsType<Assessment>(okResult.Value);

        var nf = _controller.GetAssessment(999);
        Assert.IsType<NotFoundObjectResult>(nf.Result);
    }

    [Fact]
    public void TestRunsCRUD()
    {
        var runs = _controller.GetAssessmentRuns(1);
        Assert.Null(runs.Result);

        var nf = _controller.GetAssessmentRuns(999);
        Assert.IsType<NotFoundObjectResult>(nf.Result);

        var created = _controller.CreateAssessmentRun(1, new Model.DTO.AssessmentRunDto
        {
            AssessmentId = 1,
            EntityId = 1,
            AnalystId = 1,
            HostId = 1,
            Comments = "new"
        });
        Assert.Null(created.Result);

        var updOk = _controller.UpdateAssessmentRun(1, 1, new Model.DTO.AssessmentRunDto
        {
            Id = 1, AssessmentId = 1, EntityId = 1, AnalystId = 1, HostId = 1
        });
        Assert.IsType<OkObjectResult>(updOk.Result);

        var delOk = _controller.DeleteAssessmentRun(1, 1);
        Assert.IsType<OkObjectResult>(delOk);
    }

    [Fact]
    public void TestRunAnswers()
    {
        var answers = _controller.GetAssessmentRunsQuestions(1, 1);
        Assert.Null(answers.Result);

        var created = _controller.CreateAssessmentRunsQuestion(1, 1, new Model.DTO.AssessmentRunsAnswerDto
        {
            RunId = 1, QuestionId = 1, AnswerId = 1
        });
        Assert.Null(created.Result);

        var del = _controller.CreateAssessmentRunsQuestion(1, 1, 1);
        Assert.IsType<OkObjectResult>(del.Result);

        var delAll = _controller.DeleteAllRunAnswers(1, 1);
        Assert.IsType<OkObjectResult>(delAll.Result);
    }

    [Fact]
    public void TestAssessmentCRUD()
    {
        var created = _controller.CreateAssessment(new Assessment { Name = "A3", Created = DateTime.Now });
        Assert.IsType<CreatedResult>(created.Result);

        var delOk = _controller.DeleteAssessment(1);
        Assert.IsType<OkResult>(delOk);
    }

    [Fact]
    public void TestQuestionsListAndCreate()
    {
        var list = _controller.ListAssessmentQuestions(1);
        Assert.Null(list.Result);

        var nf = _controller.ListAssessmentQuestions(99);
        Assert.IsType<NotFoundObjectResult>(nf.Result);

        var conflict = _controller.CreateAssessmentQuestion(1, new DAL.EntitiesDto.AssessmentQuestionDto
        {
            Id = 0, AssessmentId = 1, Question = "dup", Order = 3
        });
        Assert.IsType<ConflictObjectResult>(conflict.Result);

        var created = _controller.CreateAssessmentQuestion(1, new DAL.EntitiesDto.AssessmentQuestionDto
        {
            Id = 0, AssessmentId = 1, Question = "new", Order = 3
        });
        Assert.IsType<CreatedResult>(created.Result);
    }

    [Fact]
    public void TestQuestionUpdateAndDelete()
    {
        var bad = _controller.UpdateAssessmentQuestion(1, new DAL.EntitiesDto.AssessmentQuestionDto
        {
            Id = 0, AssessmentId = 1, Question = "Q1", Order = 1
        });
        Assert.IsType<ConflictObjectResult>(bad.Result);

        var notFound = _controller.UpdateAssessmentQuestion(1, new DAL.EntitiesDto.AssessmentQuestionDto
        {
            Id = 999, AssessmentId = 1, Question = "Qx", Order = 1
        });
        Assert.IsType<NotFoundObjectResult>(notFound.Result);

        var ok = _controller.UpdateAssessmentQuestion(1, new DAL.EntitiesDto.AssessmentQuestionDto
        {
            Id = 1, AssessmentId = 1, Question = "Q1-upd", Order = 1
        });
        Assert.IsType<OkObjectResult>(ok.Result);

        var delAns = _controller.DeleteAssessmentAnswer(1, 1, 1);
        Assert.IsType<OkObjectResult>(delAns.Result);

        var delQ = _controller.DeleteAssessmentQuestion(1, 1);
        Assert.IsType<OkObjectResult>(delQ.Result);
    }

    [Fact]
    public void TestAnswersCreateAndUpdate()
    {
        var createConflict = _controller.CreateAssessmentAnswers(1, 1, new[]
        {
            new DAL.EntitiesDto.AssessmentAnswerDto { Id = 0, AssessmentId = 1, QuestionId = 1, Answer = "dup" }
        });
        Assert.IsType<ConflictObjectResult>(createConflict.Result);

        var createOk = _controller.CreateAssessmentAnswers(1, 1, new[]
        {
            new DAL.EntitiesDto.AssessmentAnswerDto { Id = 0, AssessmentId = 1, QuestionId = 1, Answer = "new" }
        });
        Assert.IsType<CreatedResult>(createOk.Result);

        var updateConflict = _controller.UpdateAssessmentAnswers(1, 1, new[]
        {
            new DAL.EntitiesDto.AssessmentAnswerDto { Id = 999, AssessmentId = 1, QuestionId = 1, Answer = "x" }
        });
        Assert.IsType<ConflictObjectResult>(updateConflict.Result);

        var updateOk = _controller.UpdateAssessmentAnswers(1, 1, new[]
        {
            new DAL.EntitiesDto.AssessmentAnswerDto { Id = 1, AssessmentId = 1, QuestionId = 1, Answer = "A1" }
        });
        Assert.IsType<OkObjectResult>(updateOk.Result);
    }

    [Fact]
    public void TestListAnswers()
    {
        var ok = _controller.ListAssessmentAnswers(1);
        Assert.Null(ok.Result);

        var nf = _controller.ListAssessmentAnswers(999);
        Assert.IsType<NotFoundObjectResult>(nf.Result);
    }
}
