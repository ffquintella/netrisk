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
        
        return risksService;
    }
}