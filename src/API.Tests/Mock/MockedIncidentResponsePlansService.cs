using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        incidentResponsePlansService.GetByIdAsync(1000, true).Returns<IncidentResponsePlan>( x => throw new DataNotFoundException("IncidentResponsePlan", "1000"));
        
        incidentResponsePlansService.GetTaskByIdAsync(1).Returns(GetIncidentResponsePlans()[1].Tasks.ToList()[0]);
        
        incidentResponsePlansService.CreateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>()).Returns(async (callInfo) =>
        {
            var t = callInfo.Arg<IncidentResponsePlanTask>();
            var irp = GetIncidentResponsePlans().FirstOrDefault(x => x.Id == t.PlanId);
            if (irp == null) throw new DataNotFoundException("IncidentResponsePlan", t.Id.ToString());

            t.Id = irp.Tasks.Count + 1;
            irp.Tasks.Add(t);
            return await Task.FromResult(t);
        });
        
        incidentResponsePlansService.UpdateTaskAsync(Arg.Any<IncidentResponsePlanTask>(), Arg.Any<User>()).Returns(async (callInfo) =>
        {
            var t = callInfo.Arg<IncidentResponsePlanTask>();
            var irp = GetIncidentResponsePlans().FirstOrDefault(x => x.Id == t.PlanId);
            if (irp == null) throw new DataNotFoundException("IncidentResponsePlan", t.Id.ToString());

            var task = irp.Tasks.FirstOrDefault(x => x.Id == t.Id);
            if (task == null) throw new DataNotFoundException("IncidentResponsePlanTask", t.Id.ToString());
        });
        
        incidentResponsePlansService.GetTaskByIdAsync(1).Returns(GetIncidentResponsePlans()[1].Tasks.ToList()[0]);
        
        incidentResponsePlansService.DeleteTaskAsync(1).Returns(async (callInfo) =>
        {
            var irp = GetIncidentResponsePlans()[1];

            var task = irp.Tasks.FirstOrDefault(x => x.Id == callInfo.Arg<int>());
            if (task == null) throw new DataNotFoundException("IncidentResponsePlanTask", callInfo.Arg<int>().ToString());

        });

        incidentResponsePlansService.GetExecutionByIdAsync(1).Returns(GetIncidentResponsePlans()[1].Executions.ToList()[0]);
        
        return incidentResponsePlansService;
    }
    
    private static List<IncidentResponsePlan> GetIncidentResponsePlans()
    {
        return new List<IncidentResponsePlan>
        {
            new()
            {
                Id = 1,
                Name = "Test",
                Description = "Test",
                CreationDate = DateTime.Now,
                LastUpdate = DateTime.Now,
                HasBeenExercised = false,
                HasBeenReviewed = false,
                HasBeenApproved = false,
                CreatedById = 1,
                UpdatedById = 1,
            },
            new()
            {
                Id = 2,
                Name = "Test2",
                Description = "Test2",
                CreationDate = DateTime.Now,
                LastUpdate = DateTime.Now,
                HasBeenExercised = false,
                HasBeenReviewed = false,
                HasBeenApproved = false,
                CreatedById = 1,
                UpdatedById = 1,
                Executions = new List<IncidentResponsePlanExecution>()
                {
                    new ()
                    {
                        Id = 1,
                        ExecutionDate = DateTime.Now,
                        PlanId = 2,
                        ExecutedById = 1
                    }
                },
                Tasks = new List<IncidentResponsePlanTask>()
                {
                    new()
                    {
                        Id = 1,
                        Description = "Task 1",
                        CreationDate = DateTime.Now,
                        LastUpdate = DateTime.Now,
                        CreatedById = 1,
                        UpdatedById = 1,
                        PlanId = 2
                    },
                    new()
                    {
                        Id = 2,
                        Description = "Task 2",
                        CreationDate = DateTime.Now,
                        LastUpdate = DateTime.Now,
                        CreatedById = 1,
                        UpdatedById = 1,
                        PlanId = 2
                    }
                }
            }
        };
    }
}