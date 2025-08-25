using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using Model.DTO;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedAssessmentsService
{
    public static IAssessmentsService Create()
    {
        var svc = Substitute.For<IAssessmentsService>();

        var assessments = new List<Assessment>
        {
            new() { Id = 1, Name = "A1", Created = DateTime.Now },
            new() { Id = 2, Name = "A2", Created = DateTime.Now }
        };

        var questions = new List<AssessmentQuestion>
        {
            new() { Id = 1, AssessmentId = 1, Question = "Q1", Order = 1 },
            new() { Id = 2, AssessmentId = 1, Question = "Q2", Order = 2 }
        };

        var answers = new List<AssessmentAnswer>
        {
            new() { Id = 1, AssessmentId = 1, QuestionId = 1, Answer = "A1" },
            new() { Id = 2, AssessmentId = 1, QuestionId = 1, Answer = "A2" }
        };

        var runs = new List<AssessmentRun>
        {
            new() { Id = 1, AssessmentId = 1, EntityId = 1, AnalystId = 1, HostId = 1, Comments = "R1", Status = 0, RunDate = DateTime.Now },
            new() { Id = 2, AssessmentId = 1, EntityId = 1, AnalystId = 1, HostId = 1, Comments = "R2", Status = 0, RunDate = DateTime.Now }
        };

        var runAnswers = new List<AssessmentRunsAnswer>
        {
            new() { Id = 1, RunId = 1, QuestionId = 1, AnswerId = 1 }
        };

        // Simple returns
        svc.List().Returns(assessments);
        svc.Get(1).Returns(assessments[0]);
        svc.Get(999).Returns((Assessment?)null);

        svc.GetRuns(1).Returns(runs);
        svc.GetRuns(999).Returns((List<AssessmentRun>?)null);

        svc.GetRun(1).Returns(runs[0]);
        svc.GetRun(999).Returns((AssessmentRun?)null);

        svc.Create(Arg.Any<Assessment>()).Returns(_ => new Tuple<int, Assessment?>(0, new Assessment { Id = 3, Name = "A3", Created = DateTime.Now }));
        svc.Delete(Arg.Any<Assessment>()).Returns(0);

        svc.CreateRun(Arg.Any<AssessmentRunDto>()).Returns(ci =>
        {
            var dto = ci.Arg<AssessmentRunDto>();
            return new AssessmentRun
            {
                Id = 3,
                AssessmentId = dto.AssessmentId,
                AnalystId = dto.AnalystId,
                EntityId = dto.EntityId,
                HostId = dto.HostId,
                Comments = dto.Comments,
                Status = 0,
                RunDate = dto.RunDate
            };
        });

        svc.CreateRun(Arg.Any<AssessmentRun>()).Returns(ci => ci.Arg<AssessmentRun>());
        // Update and deletes are void; do nothing

        svc.GetRunsAnswers(1).Returns(runAnswers);
        svc.GetRunsAnswers(999).Returns((List<AssessmentRunsAnswer>?)new List<AssessmentRunsAnswer>());

        svc.GetAnswers(1).Returns(answers);
        svc.GetAnswers(999).Returns((List<AssessmentAnswer>?)null);

        svc.GetQuestions(1).Returns(questions);
        svc.GetQuestions(99).Returns((List<AssessmentQuestion>?)null);

        svc.GetQuestion(1, "Q1").Returns(questions[0]);
        svc.GetQuestion(1, "dup").Returns(questions[0]);
        svc.GetQuestion(1, "new").Returns((AssessmentQuestion?)null);

        svc.GetQuestionById(1).Returns(questions[0]);
        svc.GetQuestionById(2).Returns(questions[1]);
        svc.GetQuestionById(999).Returns((AssessmentQuestion?)null);

        svc.GetQuestionById(1, 1).Returns(questions[0]);
        svc.GetQuestionById(1, 999).Returns((AssessmentQuestion?)null);

        svc.GetAnswer(1, 1, "A1").Returns(answers[0]);
        svc.GetAnswer(1, 1, "dup").Returns(answers[0]);
        svc.GetAnswer(1, 1, "new").Returns((AssessmentAnswer?)null);

        svc.GetAnswerById(1).Returns(answers[0]);
        svc.GetAnswerById(999).Returns((AssessmentAnswer?)null);

        svc.SaveQuestion(Arg.Any<AssessmentQuestion>()).Returns(ci => ci.Arg<AssessmentQuestion>());
        svc.SaveAnswer(Arg.Any<AssessmentAnswer>()).Returns(ci => ci.Arg<AssessmentAnswer>());

        svc.DeleteAnswer(Arg.Any<AssessmentAnswer>()).Returns(0);
        svc.DeleteQuestion(Arg.Any<AssessmentQuestion>()).Returns(0);

        return svc;
    }
}
