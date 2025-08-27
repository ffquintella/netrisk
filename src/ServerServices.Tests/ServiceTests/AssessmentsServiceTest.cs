using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(AssessmentsService))]
public class AssessmentsServiceTest: BaseServiceTest
{
    private readonly IAssessmentsService _svc;

    public AssessmentsServiceTest()
    {
        _svc = _serviceProvider.GetRequiredService<IAssessmentsService>();
    }

    [Fact]
    public void TestListAndGet()
    {
        var list = _svc.List();
        Assert.NotNull(list);
        Assert.True(list.Count >= 2);

        var a1 = _svc.Get(1);
        Assert.NotNull(a1);
        Assert.Equal(1, a1!.Id);
    }

    [Fact]
    public void TestGetRuns()
    {
        var runs = _svc.GetRuns(1);
        Assert.NotNull(runs);
        Assert.True(runs!.Count >= 1);

        Assert.Throws<DataNotFoundException>(() => _svc.GetRuns(999));
    }

    [Fact]
    public void TestCreateAndUpdateRun()
    {
        try
        {
            var dto = new Model.DTO.AssessmentRunDto
            {
                Id = 0,
                AssessmentId = 1,
                EntityId = 1,
                AnalystId = 1,
                HostId = 1,
                Comments = "New"
            };

            var created = _svc.CreateRun(dto);
            Assert.NotNull(created);
            Assert.Equal(1, created.AssessmentId);

            // update existing id 1
            dto.Id = 1;
            dto.Comments = "Updated";
            _svc.UpdateRun(dto);
        }
        catch (Exception ex)
        {
            Assert.True(false, ex.ToString());
        }
    }

    [Fact]
    public void TestGetRunAndDeleteRun()
    {
        var run = _svc.GetRun(1);
        Assert.NotNull(run);
        Assert.Equal(1, run!.Id);

        _svc.DeleteRun(2);
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteRun(2));
    }

    [Fact]
    public void TestRunAnswersCRUD()
    {
        try
        {
            var answers = _svc.GetRunsAnswers(1);
            Assert.NotNull(answers);
            var initial = answers!.Count;

            var created = _svc.CreateRunAnswer(new AssessmentRunsAnswer
            {
                Id = 0,
                RunId = 1,
                QuestionId = 1,
                AnswerId = 1
            });
            Assert.NotNull(created);

            var afterCreate = _svc.GetRunsAnswers(1);
            Assert.NotNull(afterCreate);
            Assert.Equal(initial + 1, afterCreate!.Count);

            _svc.DeleteRunAnswer(1, 1, created.Id);
            var afterDelete = _svc.GetRunsAnswers(1);
            Assert.NotNull(afterDelete);
            Assert.Equal(initial, afterDelete!.Count);

            // bulk delete should not throw
            _svc.DeleteAllRunAnswer(1, 1);
        }
        catch (Exception ex)
        {
            Assert.True(false, ex.ToString());
        }
    }

    [Fact]
    public void TestAssessmentCRUD()
    {
        var tuple = _svc.Create(new Assessment { Id = 0, Name = "A3", Created = DateTime.Now });
        Assert.Equal(0, tuple.Item1);
        Assert.NotNull(tuple.Item2);

        var upd = tuple.Item2!;
        upd.Name = "A3-upd";
        _svc.Update(upd);

        var delResult = _svc.Delete(upd);
        Assert.Equal(0, delResult);
    }

    [Fact]
    public void TestQuestionsAndAnswers()
    {
        var questions = _svc.GetQuestions(1);
        Assert.NotNull(questions);
        Assert.True(questions!.Count >= 2);

        var qByText = _svc.GetQuestion(1, "Q1");
        Assert.NotNull(qByText);

        var qById = _svc.GetQuestionById(1);
        Assert.NotNull(qById);

        var qByIds = _svc.GetQuestionById(1, 1);
        Assert.NotNull(qByIds);

        var savedQ = _svc.SaveQuestion(new AssessmentQuestion { Id = 0, AssessmentId = 1, Question = "Q-new", Order = 3 });
        Assert.NotNull(savedQ);

        var answers = _svc.GetQuestionAnswers(1);
        Assert.NotNull(answers);
        Assert.True(answers!.Count >= 2);

        var ansByText = _svc.GetAnswer(1, 1, "A1");
        Assert.NotNull(ansByText);

        var ansById = _svc.GetAnswerById(1);
        Assert.NotNull(ansById);

        var savedA = _svc.SaveAnswer(new AssessmentAnswer
        {
            Id = 0,
            AssessmentId = 1,
            QuestionId = 1,
            Answer = "A-new",
            SubmitRisk = false,
            RiskSubject = Array.Empty<byte>(),
            RiskScore = 1.0f,
            AssessmentScoringId = 1,
            Order = 5
        });
        Assert.NotNull(savedA);

        var delARes = _svc.DeleteAnswer(savedA!);
        Assert.Equal(0, delARes);

        var delQRes = _svc.DeleteQuestion(savedQ!);
        Assert.Equal(0, delQRes);
    }
}
