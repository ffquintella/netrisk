using System;
using System.Collections.Generic;
using DAL.Entities;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit.Sdk;

namespace API.Tests.Mock;

public class MockedIncidentResponsePlansService
{
    public static IIncidentResponsePlansService Create()
    {
        var incidentResponsePlansService = Substitute.For<IIncidentResponsePlansService>();

        incidentResponsePlansService.GetAllAsync().Returns(GetIncidentResponsePlans());
        
        incidentResponsePlansService.GetByIdAsync(1).Returns(GetIncidentResponsePlans()[0]);
        incidentResponsePlansService.GetByIdAsync(1000).Returns<IncidentResponsePlan>( x => throw new DataNotFoundException("IncidentResponsePlan", "1000"));
        
        incidentResponsePlansService.GetByIdAsync(2, true).Returns(GetIncidentResponsePlans()[1]);
        
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
                UpdatedById = 1,
                Tasks = new List<IncidentResponsePlanTask>()
                {
                    new()
                    {
                        Id = 1,
                        Description = "Task 1",
                        CreationDate = DateTime.Now,
                        LastUpdate = DateTime.Now,
                        CreatedById = 1,
                        UpdatedById = 1
                    },
                    new()
                    {
                        Id = 2,
                        Description = "Task 2",
                        CreationDate = DateTime.Now,
                        LastUpdate = DateTime.Now,
                        CreatedById = 1,
                        UpdatedById = 1
                    }
                }
            }
        };
    }
}