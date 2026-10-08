using System;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.ThirdParties;
using Xunit;

namespace GUIClient.Tests.Track9;

public class Track9ThirdPartyFormsTest
{
    [Fact]
    public void TestBuildRequestKeepsEveryContractField()
    {
        var source = new ThirdPartyDto
        {
            Name = "Cloud Co",
            LegalName = "Cloud Company SA",
            TaxId = "123",
            Country = "BR",
            Description = "Hosting",
            Website = "https://cloud.example",
            EntityId = 7,
            OwnerId = 8,
            Status = ThirdPartyStatus.Exiting,
            IsCloudProvider = true,
            IsIdentityProvider = true,
            ProcessesPersonalData = false,
            ContractReference = "C-1",
            ContractStart = new DateTime(2026, 1, 1),
            ContractEnd = new DateTime(2027, 1, 1),
            SlaAvailabilityPercent = 99.95m,
            ContractedRtoMinutes = 60,
            ContractedRpoMinutes = 15,
            VulnerabilityFixDays = 30,
            RightToAudit = true,
            AuditClauseReference = "clause 9",
            ExitPlan = "move workloads",
            ExitPlanReviewedAt = new DateTime(2026, 2, 1),
            ExitPlanTestedAt = new DateTime(2026, 3, 1),
            DataPortability = "open export"
        };

        var request = Track9ThirdPartyForms.BuildRequest(source);

        Assert.Equal("Cloud Co", request.Name);
        Assert.Equal("Cloud Company SA", request.LegalName);
        Assert.Equal("123", request.TaxId);
        Assert.Equal("BR", request.Country);
        Assert.Equal("Hosting", request.Description);
        Assert.Equal("https://cloud.example", request.Website);
        Assert.Equal(7, request.EntityId);
        Assert.Equal(8, request.OwnerId);
        Assert.Equal(ThirdPartyStatus.Exiting, request.Status);
        Assert.True(request.IsCloudProvider);
        Assert.True(request.IsIdentityProvider);
        Assert.False(request.ProcessesPersonalData);
        Assert.Equal("C-1", request.ContractReference);
        Assert.Equal(new DateTime(2026, 1, 1), request.ContractStart);
        Assert.Equal(new DateTime(2027, 1, 1), request.ContractEnd);
        Assert.Equal(99.95m, request.SlaAvailabilityPercent);
        Assert.Equal(60, request.ContractedRtoMinutes);
        Assert.Equal(15, request.ContractedRpoMinutes);
        Assert.Equal(30, request.VulnerabilityFixDays);
        Assert.True(request.RightToAudit);
        Assert.Equal("clause 9", request.AuditClauseReference);
        Assert.Equal("move workloads", request.ExitPlan);
        Assert.Equal(new DateTime(2026, 2, 1), request.ExitPlanReviewedAt);
        Assert.Equal(new DateTime(2026, 3, 1), request.ExitPlanTestedAt);
        Assert.Equal("open export", request.DataPortability);
    }

    [Fact]
    public void TestAssessmentAnswersKeepExplicitBlankQuestions()
    {
        var request = Track9ThirdPartyForms.BuildAnswers([
            new HecvatAnswerDto
            {
                QuestionId = "Q1",
                Answer = HecvatAnswer.Unanswered,
                PreferredAnswer = HecvatAnswer.Yes,
                Weight = 3,
                Critical = true,
                Notes = "Vendor left blank"
            }
        ]);

        var answer = Assert.Single(request.Answers!);
        Assert.Equal(HecvatAnswer.Unanswered, answer.Answer);
        Assert.Equal(HecvatAnswer.Yes, answer.PreferredAnswer);
        Assert.Equal(3, answer.Weight);
        Assert.True(answer.Critical);
    }
}
