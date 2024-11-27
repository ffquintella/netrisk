using System;
using System.Collections.Generic;
using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public class MockedIncidentResponsePlansService
{
    public static IIncidentResponsePlansService Create()
    {
        var incidentResponsePlansService = Substitute.For<IIncidentResponsePlansService>();

        incidentResponsePlansService.GetAllAsync().Returns(GetIncidentResponsePlans());
        
        return incidentResponsePlansService;
    }
    
    private static List<IncidentResponsePlan> GetIncidentResponsePlans()
    {
        return new List<IncidentResponsePlan>
        {
            new()
            {
                Id = 1,
                Name = "Teste",
                Description = "Teste",
                CreationDate = DateTime.Now,
                LastUpdate = DateTime.Now,
                HasBeenExercised = false,
                HasBeenReviewed = false,
                HasBeenApproved = false,
                CreatedById = 1,
                UpdatedById = 1
            },
            new()
            {
                Id = 2,
                Name = "Teste2",
                Description = "Teste2",
                CreationDate = DateTime.Now,
                LastUpdate = DateTime.Now,
                HasBeenExercised = false,
                HasBeenReviewed = false,
                HasBeenApproved = false,
                CreatedById = 1,
                UpdatedById = 1
            }
        };
    }
}