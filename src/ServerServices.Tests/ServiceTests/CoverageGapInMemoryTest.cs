using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class RisksServiceGapInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRisksService _svc;
    public RisksServiceGapInMemoryTest() => _svc = GetService<IRisksService>();

    private static Risk NewRisk(int id, string status = "Open", int owner = 1) => new()
    {
        Id = id, Status = status, Subject = $"R{id}", ReferenceId = "R", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", Owner = owner, Manager = owner, SubmittedBy = owner,
        Source = 1, Category = 1
    };

    private void SeedLookups() => Seed(ctx =>
    {
        ctx.Sources.Add(new Source { Value = 1, Name = "S" });
        ctx.Categories.Add(new Category { Value = 1, Name = "C" });
    });

    [Fact]
    public void TestGetUserRiskAdminOwnerUnauthorized()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Roles.Add(new Role { Value = 1, Name = "R" });
            ctx.Users.Add(new User { Value = 2, Name = "owner", Type = "local", Email = "u@x.io", Password = new byte[] { 1 }, Enabled = true });
            ctx.Users.Add(new User { Value = 3, Name = "other", Type = "local", Email = "u@x.io", Password = new byte[] { 1 }, Enabled = true });
            ctx.Risks.Add(NewRisk(1, owner: 2));
        });

        // admin sees any risk
        Assert.Equal(1, _svc.GetUserRisk(new User { Value = 9, Admin = true }, 1).Id);
        // owner sees own risk
        Assert.Equal(1, _svc.GetUserRisk(new User { Value = 2, Admin = false, RoleId = 1 }, 1).Id);
        // unrelated non-admin is blocked
        Assert.Throws<UserNotAuthorizedException>(() =>
            _svc.GetUserRisk(new User { Value = 3, Admin = false, RoleId = 1 }, 1));
        Assert.Throws<InvalidParameterException>(() => _svc.GetUserRisk(null!, 1));
    }

    [Fact]
    public void TestGetUserRisksStatusCombinations()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "Open", owner: 5));
            ctx.Risks.Add(NewRisk(2, status: "New", owner: 5));
        });
        var admin = new User { Value = 9, Admin = true };

        Assert.NotEmpty(_svc.GetUserRisks(admin, "Open", "Closed"));
        Assert.NotEmpty(_svc.GetUserRisks(admin, "New", null));
        Assert.NotEmpty(_svc.GetUserRisks(admin, null, "Closed"));
    }

    [Fact]
    public void TestGetAllVariants()
    {
        SeedLookups();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, status: "Open"));
            ctx.Risks.Add(NewRisk(2, status: "Closed"));
            ctx.Risks.Add(NewRisk(3, status: "New"));
        });

#pragma warning disable CS0618
        Assert.Equal(2, _svc.GetAll().Count);                  // not Closed
        Assert.Single(_svc.GetAll("Open", "Closed"));
        Assert.Single(_svc.GetAll("New"));
        Assert.Equal(3, _svc.GetAll(null, null).Count);
#pragma warning restore CS0618
    }

    [Fact]
    public void TestGetToReview()
    {
        SeedLookups();
        Seed(ctx =>
        {
            var risk = NewRisk(1, status: "Open");
            risk.MgmtReviews.Add(new MgmtReview
            {
                Id = 1, RiskId = 1, Review = 1, Reviewer = 1, NextStep = 1, Comments = "",
                SubmissionDate = DateTime.Now.AddDays(-100)
            });
            ctx.Risks.Add(risk);
        });

        var toReview = _svc.GetToReview(30);

        Assert.Single(toReview);
    }
}

public class IrpExecutionGapInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIncidentResponsePlansService _svc;
    private static readonly User Admin = new() { Value = 1, Name = "Admin", Type = "local", Admin = true, Lang = "en" };

    public IrpExecutionGapInMemoryTest() => _svc = GetService<IIncidentResponsePlansService>();

    [Fact]
    public async Task TestTaskExecutionLifecycle()
    {
        var incident = new Incident { Id = 1, Description = "d", Name = "INC" };
        var plan = new IncidentResponsePlan
            { Id = 1, Name = "IRP", Description = "d", CreationDate = DateTime.Now, LastUpdate = DateTime.Now };
        incident.IncidentResponsePlansActivated.Add(plan);
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(plan);
            ctx.IncidentResponsePlanTasks.Add(new IncidentResponsePlanTask { Id = 1, PlanId = 1, Name = "T", AssignedToId = 50 });
            ctx.IncidentResponsePlanExecutions.Add(new IncidentResponsePlanExecution
            {
                Id = 1, PlanId = 1, ExecutionTrigger = "m", ExecutionResult = "-", Duration = TimeSpan.Zero
            });
            // Entity 50 supplies the assignee e-mail used by CreateTaskExecutionAsync.
            var entity = new Entity { Id = 50, DefinitionName = "person", DefinitionVersion = "1", Status = "active",
                Created = DateTime.Now, Updated = DateTime.Now };
            entity.EntitiesProperties.Add(new EntitiesProperty { Id = 1, Entity = 50, Type = "email", Value = "a@b.io", OldValue = "", Name = "email" });
            ctx.Entities.Add(entity);
            ctx.Incidents.Add(incident);
        });

        var te = new IncidentResponsePlanTaskExecution
        {
            Id = 0, PlanExecutionId = 1, TaskId = 1, ExecutionDate = DateTime.Now, Duration = TimeSpan.Zero
        };
        var created = await _svc.CreateTaskExecutionAsync(te, incident, Admin);
        Assert.True(created.Id > 0);

        Assert.Equal(created.Id, (await _svc.GetTaskExecutionByIdAsync(created.Id)).Id);
        Assert.NotEmpty(await _svc.GetTaskExecutionsByIdAsync(created.Id));

        await _svc.ChangeExecutionTaskSatusByIdAsync(created.Id, (int)IntStatus.Completed);
        Assert.Equal((int)IntStatus.Completed, (await _svc.GetTaskExecutionByIdAsync(created.Id)).Status);

        var incidentByTask = await _svc.GetIncidentByTaskIdAsync(1);
        Assert.NotNull(incidentByTask);

        await _svc.DeleteTaskExecutionAsync(created.Id);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetTaskExecutionByIdAsync(created.Id));
    }

    [Fact]
    public async Task TestCreateTaskExecutionNotFoundPaths()
    {
        var incident = new Incident { Id = 1, Description = "d", Name = "INC" };

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.CreateTaskExecutionAsync(
            new IncidentResponsePlanTaskExecution { PlanExecutionId = 99, TaskId = 1 }, incident, Admin));

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.ChangeExecutionTaskSatusByIdAsync(99, 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetIncidentByTaskIdAsync(99));
    }
}
