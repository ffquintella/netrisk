using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class AssessmentsServiceGapInMemoryTest : InMemoryServiceTestBase
{
    private readonly IAssessmentsService _svc;
    public AssessmentsServiceGapInMemoryTest() => _svc = GetService<IAssessmentsService>();

    private static Assessment NewAssessment(int id, string name = "A") =>
        new() { Id = id, Name = name, Created = new DateTime(2026, 1, 1) };

    private static AssessmentQuestion NewQuestion(int id, int assessmentId, string q = "Q") =>
        new() { Id = id, AssessmentId = assessmentId, Question = q, Order = 1 };

    private static AssessmentAnswer NewAnswer(int id, int assessmentId, int questionId, string text = "Ans") =>
        new() { Id = id, AssessmentId = assessmentId, QuestionId = questionId, Answer = text,
            RiskSubject = Array.Empty<byte>(), Order = 1 };

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "person", DefinitionVersion = "1", Status = "active",
        Created = new DateTime(2026, 1, 1), Updated = new DateTime(2026, 1, 1)
    };

    private static User NewUser(int value) => new()
    {
        Value = value, Name = "U", Type = "local", Enabled = true, Email = "u@x.io", Password = new byte[] { 1 }
    };

    private static Host NewHost(int id) => new()
    {
        Id = id, Ip = "1.1.1.1", Status = 1, Source = "manual", RegistrationDate = new DateTime(2026, 1, 1)
    };

    [Fact]
    public void TestAssessmentCreateUpdateDelete()
    {
        var (code, created) = _svc.Create(NewAssessment(0, "Created"));
        Assert.Equal(0, code);
        Assert.NotNull(created);

        Assert.NotEmpty(_svc.List());
        Assert.NotNull(_svc.Get(created!.Id));

        var update = NewAssessment(created.Id, "Renamed");
        _svc.Update(update);
        Assert.Equal("Renamed", _svc.Get(created.Id)!.Name);
        Assert.Throws<DatabaseException>(() => _svc.Update(NewAssessment(999)));

        Assert.Equal(0, _svc.Delete(_svc.Get(created.Id)!));
    }

    [Fact]
    public void TestQuestionAndAnswerCrud()
    {
        Seed(ctx => ctx.Assessments.Add(NewAssessment(1)));

        var question = _svc.SaveQuestion(NewQuestion(0, 1, "What?"));
        Assert.NotNull(question);
        Assert.True(question!.Id > 0);

        // update existing question
        var q2 = NewQuestion(question.Id, 1, "Changed?");
        _svc.SaveQuestion(q2);
        Assert.Equal("Changed?", _svc.GetQuestionById(question.Id)!.Question);

        Assert.NotNull(_svc.GetQuestion(1, "Changed?"));
        Assert.NotNull(_svc.GetQuestionById(1, question.Id));
        Assert.NotNull(_svc.GetQuestions(1));
        Assert.Throws<InvalidReferenceException>(() => _svc.SaveQuestion(NewQuestion(0, 999)));

        var answer = _svc.SaveAnswer(NewAnswer(0, 1, question.Id, "Yes"));
        Assert.NotNull(answer);

        Assert.NotNull(_svc.GetAnswerById(answer!.Id));
        Assert.NotNull(_svc.GetAnswer(1, question.Id, "Yes"));
        Assert.Single(_svc.GetAnswers(1)!);
        Assert.Single(_svc.GetQuestionAnswers(question.Id)!);
        Assert.Throws<InvalidReferenceException>(() => _svc.SaveAnswer(NewAnswer(0, 999, question.Id)));

        Assert.Equal(0, _svc.DeleteAnswer(answer));
        Assert.Equal(0, _svc.DeleteQuestion(question));
    }

    [Fact]
    public void TestRunCreateUpdateDelete()
    {
        Seed(ctx =>
        {
            ctx.Assessments.Add(NewAssessment(1));
            ctx.Entities.Add(NewEntity(1));
            ctx.Users.Add(NewUser(2));
            ctx.Hosts.Add(NewHost(3));
        });

        var dto = new AssessmentRunDto
        {
            Id = 0, AssessmentId = 1, EntityId = 1, AnalystId = 2, HostId = 3,
            RunDate = new DateTime(2026, 1, 1), Comments = "c"
        };
        var run = _svc.CreateRun(dto);
        Assert.True(run.Id > 0);

        Assert.NotNull(_svc.GetRun(run.Id));
        Assert.Single(_svc.GetRuns(1));

        var updateDto = new AssessmentRunDto
        {
            Id = run.Id, AssessmentId = 1, EntityId = 1, AnalystId = 2, HostId = 3, Comments = "updated"
        };
        _svc.UpdateRun(updateDto);

        _svc.DeleteRun(run.Id);
        Assert.Empty(_svc.GetRuns(1));
    }

    [Fact]
    public void TestRunCreateMissingReferencesThrow()
    {
        var dto = new AssessmentRunDto { Id = 0, AssessmentId = 999, EntityId = 1, AnalystId = 2 };
        Assert.Throws<DataNotFoundException>(() => _svc.CreateRun(dto));
    }

    [Fact]
    public void TestRunAnswerCrud()
    {
        Seed(ctx =>
        {
            ctx.Assessments.Add(NewAssessment(1));
            ctx.Entities.Add(NewEntity(1));
            ctx.Users.Add(NewUser(2));
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = 1, AssessmentId = 1, EntityId = 1, Status = 0 });
            ctx.AssessmentQuestions.Add(NewQuestion(1, 1));
            ctx.AssessmentAnswers.Add(NewAnswer(1, 1, 1));
        });

        var answer = _svc.CreateRunAnswer(new AssessmentRunsAnswer { Id = 0, RunId = 1, AnswerId = 1, QuestionId = 1 });
        Assert.True(answer.Id > 0);

        Assert.Single(_svc.GetRunsAnswers(1)!);

        _svc.DeleteRunAnswer(1, 1, answer.Id);
        Assert.Empty(_svc.GetRunsAnswers(1)!);
    }

    [Fact]
    public void TestDeleteAllRunAnswers()
    {
        Seed(ctx =>
        {
            ctx.Assessments.Add(NewAssessment(1));
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = 1, AssessmentId = 1, EntityId = 1, Status = 0 });
            ctx.AssessmentQuestions.Add(NewQuestion(1, 1));
            ctx.AssessmentAnswers.Add(NewAnswer(1, 1, 1));
            ctx.AssessmentRunsAnswers.Add(new AssessmentRunsAnswer { Id = 1, RunId = 1, AnswerId = 1, QuestionId = 1 });
        });

        _svc.DeleteAllRunAnswer(1, 1);

        Assert.Empty(_svc.GetRunsAnswers(1)!);
    }

    [Fact]
    public void TestGetRunsMissingAssessmentThrows()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.GetRuns(999));
    }

    [Fact]
    public async Task TestSaveAndGetDraftAnswersAsync()
    {
        Seed(ctx =>
        {
            ctx.Assessments.Add(NewAssessment(1));
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = 10, AssessmentId = 1, EntityId = 1, Status = 0 });
            ctx.AssessmentQuestions.Add(NewQuestion(100, 1));
        });

        // Save
        var draft = await _svc.SaveDraftAnswerAsync(10, 100, "{\"value\": \"Yes\"}");
        Assert.NotNull(draft);
        Assert.True(draft.Id > 0);
        Assert.Equal("{\"value\": \"Yes\"}", draft.AnswerContentJson);

        // Update
        var draft2 = await _svc.SaveDraftAnswerAsync(10, 100, "{\"value\": \"No\"}");
        Assert.Equal(draft.Id, draft2.Id);
        Assert.Equal("{\"value\": \"No\"}", draft2.AnswerContentJson);

        // Retrieve
        var list = await _svc.GetDraftAnswersAsync(10);
        Assert.Single(list);
        Assert.Equal("{\"value\": \"No\"}", list[0].AnswerContentJson);
    }

    [Fact]
    public async Task TestGetVisibleQuestionsForPageAsync_ConditionalLogic()
    {
        Seed(ctx =>
        {
            ctx.Assessments.Add(NewAssessment(1));
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = 20, AssessmentId = 1, EntityId = 1, Status = 0 });

            // Q1: Parent/Reference question on Page 1
            ctx.AssessmentQuestions.Add(new AssessmentQuestion 
            { 
                Id = 201, 
                AssessmentId = 1, 
                Question = "Q1", 
                PageNumber = 1, 
                Order = 1 
            });

            // Q2: Conditional question on Page 2 (visible if Q1 is answered "Yes")
            ctx.AssessmentQuestions.Add(new AssessmentQuestion 
            { 
                Id = 202, 
                AssessmentId = 1, 
                Question = "Q2", 
                PageNumber = 2, 
                Order = 1,
                ConditionJson = "{\"QuestionId\": 201, \"Operator\": \"equals\", \"Value\": \"Yes\"}"
            });

            // Q3: Conditional question on Page 2 (visible if Q1 is not empty)
            ctx.AssessmentQuestions.Add(new AssessmentQuestion 
            { 
                Id = 203, 
                AssessmentId = 1, 
                Question = "Q3", 
                PageNumber = 2, 
                Order = 2,
                ConditionJson = "{\"QuestionId\": 201, \"Operator\": \"notEmpty\"}"
            });
        });

        // 1. Without any draft answers, Q2 and Q3 on Page 2 should be hidden (empty list)
        var page2QuestionsEmpty = await _svc.GetVisibleQuestionsForPageAsync(20, 2);
        Assert.Empty(page2QuestionsEmpty);

        // 2. Answer Q1 with "No" -> Q3 is visible (not empty), but Q2 is hidden (not equal "Yes")
        await _svc.SaveDraftAnswerAsync(20, 201, "No");
        var page2QuestionsWithNo = await _svc.GetVisibleQuestionsForPageAsync(20, 2);
        Assert.Single(page2QuestionsWithNo);
        Assert.Equal(203, page2QuestionsWithNo[0].Id);

        // 3. Answer Q1 with "Yes" -> both Q2 and Q3 are visible!
        await _svc.SaveDraftAnswerAsync(20, 201, "Yes");
        var page2QuestionsWithYes = await _svc.GetVisibleQuestionsForPageAsync(20, 2);
        Assert.Equal(2, page2QuestionsWithYes.Count);
        Assert.Contains(page2QuestionsWithYes, q => q.Id == 202);
        Assert.Contains(page2QuestionsWithYes, q => q.Id == 203);
    }
}
