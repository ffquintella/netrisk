using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedRisksService
{
    public static IRisksService Create()
    {
        var risksService = Substitute.For<IRisksService>();

        /*risksService.GetRiskAsync("testRisk").Returns(new Risk()
        {
            Name = "testRisk",
            Description = "testRisk"
        });*/

        risksService.GetVulnerabilitiesAsync(1, false).Returns(new List<Vulnerability>()
        {
            new ()
            {
                Id = 1,
                AnalystId = 1,
                Severity = "1",
                Score = 5
            },
            new ()
            {
                Id = 2,
                AnalystId = 1,
                Severity = "1",
                Score = 5
            }
        });
        
        risksService.GetIncidentResponsePlanAsync(1).Returns(
            new IncidentResponsePlan()
            {
                Id = 1,
                Name = "Test",
                Description = "Test",
                CreatedById = 1,
                UpdatedById = 1,
                Status = 1,
                HasBeenTested = true,
                HasBeenUpdated = true,
                HasBeenExercised = true,
                HasBeenReviewed = true,
                HasBeenApproved = true,
                LastTestDate = new DateTime(2021, 1, 1),
                LastExerciseDate = new DateTime(2021, 1, 1),
                LastReviewDate = new DateTime(2021, 1, 1),
                ApprovalDate = new DateTime(2021, 1, 1),
                LastTestedById = 1,
                LastExercisedById = 1,
                LastReviewedById = 1
            });
        
        risksService.AssocianteRiskToIncidentResponsePlanAsync(10,1).Returns(  (_) => throw new DataNotFoundException("risk", "Risk not found"));
        risksService.AssocianteRiskToIncidentResponsePlanAsync(1,100).Returns(  (_) => throw new DataNotFoundException("incidentResponsePlan", "Irp not found"));
        risksService.AssocianteRiskToIncidentResponsePlanAsync(1,1).Returns(  (_) => Task.CompletedTask);

        // --- Track 8 -----------------------------------------------------------------------------
        // Pending-risk triage, the out-of-cadence review flag and the paired scores. Deterministic
        // fixtures with one guard branch each, matching how the rest of this file behaves.

        risksService.GetPendingRisksAsync(Arg.Any<DAL.Enums.PendingRiskStatus?>())
            .Returns(Task.FromResult(new List<Model.Governance.PendingRiskListing>
            {
                new()
                {
                    Id = 1, AssessmentId = 3, AssessmentAnswerId = 4,
                    Subject = "Shared credentials in the deployment script", Score = 6.5f,
                    Status = DAL.Enums.PendingRiskStatus.Pending,
                    SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            }));

        risksService.PromotePendingRiskAsync(Arg.Any<int>(),
                Arg.Any<Model.Governance.PendingRiskPromotion>(), Arg.Any<int>())
            .Returns(call =>
            {
                var pendingId = call.ArgAt<int>(0);

                if (pendingId == 999)
                    throw new DataNotFoundException("pending_risks", "999",
                        new Exception("Pending risk not found."));

                if (pendingId == 998)
                    throw new InvalidStateTransitionException("Promoted", "Promoted",
                        "This pending risk has already been triaged.");

                return Task.FromResult(new Risk
                {
                    Id = 500, Status = "New", Subject = "Promoted", ReferenceId = "ASMT-3-4",
                    Assessment = string.Empty, Notes = string.Empty,
                    RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty
                });
            });

        risksService.DismissPendingRiskAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(call =>
            {
                var reason = call.ArgAt<string>(1);

                if (string.IsNullOrWhiteSpace(reason))
                    throw new InvalidParameterException("reason",
                        "Dismissing a pending risk needs a reason.");

                if (call.ArgAt<int>(0) == 999)
                    throw new DataNotFoundException("pending_risks", "999",
                        new Exception("Pending risk not found."));

                return Task.CompletedTask;
            });

        risksService.RequestReviewAsync(Arg.Any<int>(), Arg.Any<string>()).Returns(call =>
        {
            if (call.ArgAt<int>(0) == 999)
                throw new DataNotFoundException("risks", "999", new Exception("Risk not found."));
            return Task.FromResult(true);
        });

        risksService.GetReviewRequestedAsync().Returns(Task.FromResult(new List<Risk>()));

        risksService.GetScorePairsAsync(Arg.Any<List<int>?>())
            .Returns(Task.FromResult(new List<Model.Governance.RiskScorePair>
            {
                new() { RiskId = 1, Inherent = 8f, Residual = 3f }
            }));

        risksService.SaveRiskAsync(Arg.Any<Risk>()).Returns(Task.CompletedTask);

        return risksService;
    }
}