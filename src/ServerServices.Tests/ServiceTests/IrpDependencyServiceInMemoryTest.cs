using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Persisted task-dependency edges and the blocked-task override gate
/// (Track 2 milestone 2.4.3).
/// </summary>
public class IrpDependencyServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIrpScheduleService _svc;

    private static readonly DateTime PlanStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public IrpDependencyServiceInMemoryTest()
    {
        _svc = GetService<IIrpScheduleService>();

        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan
            {
                Id = 1, Name = "P", Description = "d",
                CreationDate = PlanStart, LastUpdate = PlanStart, CreatedById = 1
            });

            // All four sit in one stage, so without explicit edges they would run in parallel.
            for (var i = 1; i <= 4; i++)
            {
                ctx.IncidentResponsePlanTasks.Add(new IncidentResponsePlanTask
                {
                    Id = i, PlanId = 1, Name = $"T{i}", ExecutionOrder = 1,
                    EstimatedDuration = TimeSpan.FromHours(1), AssignedToId = 1, CreationDate = PlanStart
                });
            }

            ctx.Users.Add(new User
            {
                Value = 7, Name = "Operator", Login = "op", Type = "local",
                Enabled = true, Email = "op@x.io", Password = new byte[] { 1 }
            });
        });
    }

    [Fact]
    public async Task AnEdgeIsStoredAndListed()
    {
        await _svc.AddDependencyAsync(1, 2, 1);

        var edges = await _svc.GetDependenciesAsync(1);

        var edge = Assert.Single(edges);
        Assert.Equal(2, edge.TaskId);
        Assert.Equal(1, edge.DependsOnTaskId);
        Assert.Equal("T2", edge.TaskName);
        Assert.Equal("T1", edge.DependsOnTaskName);
    }

    [Fact]
    public async Task AnExplicitEdgeChangesTheSchedule()
    {
        // Same stage, so both start at zero until an edge says otherwise.
        var before = await _svc.GetScheduleAsync(1);
        Assert.Equal(TimeSpan.Zero, before!.Items.Single(i => i.TaskId == 2).EarlyStart);

        await _svc.AddDependencyAsync(1, 2, 1);

        var after = await _svc.GetScheduleAsync(1);
        var t2 = after!.Items.Single(i => i.TaskId == 2);

        Assert.Contains(1, t2.DependsOn);
        Assert.Equal(TimeSpan.FromHours(1), t2.EarlyStart);
    }

    [Fact]
    public async Task ATaskCannotDependOnItself()
    {
        await Assert.ThrowsAsync<RuleBrokenException>(() => _svc.AddDependencyAsync(1, 2, 2));
    }

    [Fact]
    public async Task ADirectCycleIsRefused()
    {
        await _svc.AddDependencyAsync(1, 2, 1);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => _svc.AddDependencyAsync(1, 1, 2));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnIndirectCycleIsRefused()
    {
        await _svc.AddDependencyAsync(1, 2, 1);
        await _svc.AddDependencyAsync(1, 3, 2);

        // 1 -> 3 would close 1 -> 3 -> 2 -> 1.
        await Assert.ThrowsAsync<RuleBrokenException>(() => _svc.AddDependencyAsync(1, 1, 3));
    }

    [Fact]
    public async Task AnEdgeToAnotherPlansTaskIsRefused()
    {
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan
            {
                Id = 2, Name = "Other", Description = "d",
                CreationDate = PlanStart, LastUpdate = PlanStart, CreatedById = 1
            });
            ctx.IncidentResponsePlanTasks.Add(new IncidentResponsePlanTask
            {
                Id = 99, PlanId = 2, Name = "Foreign", ExecutionOrder = 1, AssignedToId = 1,
                CreationDate = PlanStart
            });
        });

        await Assert.ThrowsAsync<RuleBrokenException>(() => _svc.AddDependencyAsync(1, 2, 99));
    }

    [Fact]
    public async Task AddingTheSameEdgeTwiceIsIdempotent()
    {
        await _svc.AddDependencyAsync(1, 2, 1);
        await _svc.AddDependencyAsync(1, 2, 1);

        Assert.Single(await _svc.GetDependenciesAsync(1));
    }

    [Fact]
    public async Task AnEdgeCanBeRemoved()
    {
        await _svc.AddDependencyAsync(1, 2, 1);
        await _svc.RemoveDependencyAsync(1, 2, 1);

        Assert.Empty(await _svc.GetDependenciesAsync(1));

        // Removing one that is not there is not an error.
        await _svc.RemoveDependencyAsync(1, 2, 1);
    }

    [Fact]
    public async Task TasksWithoutExplicitEdgesKeepTheirStageOrdering()
    {
        Seed(ctx => ctx.IncidentResponsePlanTasks.Add(new IncidentResponsePlanTask
        {
            Id = 5, PlanId = 1, Name = "Second stage", ExecutionOrder = 2,
            EstimatedDuration = TimeSpan.FromHours(1), AssignedToId = 1, CreationDate = PlanStart
        }));

        var schedule = await _svc.GetScheduleAsync(1);
        var stageTwo = schedule!.Items.Single(i => i.TaskId == 5);

        // Nobody declared an edge for it, so it still waits on the whole first stage.
        Assert.Equal(4, stageTwo.DependsOn.Count);
        Assert.Equal(TimeSpan.FromHours(1), stageTwo.EarlyStart);
    }

    // ---------------------------------------------------------------- override gate

    [Fact]
    public async Task CompletingABlockedTaskRecordsWhoOverrodeItAndWhy()
    {
        await _svc.AddDependencyAsync(1, 2, 1);

        var blocked = await _svc.GetScheduleAsync(1);
        Assert.True(blocked!.Items.Single(i => i.TaskId == 2).IsBlocked);

        await _svc.CompleteBlockedTaskAsync(1, 2, userId: 7, reason: "Predecessor handled out of band");

        using var ctx = OpenContext();
        var task = ctx.IncidentResponsePlanTasks.First(t => t.Id == 2);

        Assert.Equal((int)IntStatus.Closed, task.Status);
        Assert.Equal("Predecessor handled out of band", task.OverrideReason);
        Assert.Equal(7, task.OverriddenById);
        Assert.NotNull(task.OverriddenAt);
    }

    [Fact]
    public async Task ABlockedTaskCannotBeCompletedWithoutAReason()
    {
        await _svc.AddDependencyAsync(1, 2, 1);

        await Assert.ThrowsAsync<RuleBrokenException>(
            () => _svc.CompleteBlockedTaskAsync(1, 2, userId: 7, reason: "   "));

        using var ctx = OpenContext();
        Assert.NotEqual((int)IntStatus.Closed, ctx.IncidentResponsePlanTasks.First(t => t.Id == 2).Status);
    }

    [Fact]
    public async Task CompletingAnUnblockedTaskLeavesNoOverrideStamp()
    {
        // T1 waits on nothing, so this is an ordinary completion that happens to go through the
        // same call. It must not read afterwards as though a rule had been bent.
        await _svc.CompleteBlockedTaskAsync(1, 1, userId: 7, reason: "Routine");

        using var ctx = OpenContext();
        var task = ctx.IncidentResponsePlanTasks.First(t => t.Id == 1);

        Assert.Equal((int)IntStatus.Closed, task.Status);
        Assert.Null(task.OverrideReason);
        Assert.Null(task.OverriddenById);
    }

    [Fact]
    public async Task CompletingThePredecessorUnblocksTheSuccessor()
    {
        await _svc.AddDependencyAsync(1, 2, 1);

        await _svc.CompleteBlockedTaskAsync(1, 1, userId: 7, reason: "Done");

        var schedule = await _svc.GetScheduleAsync(1);
        Assert.False(schedule!.Items.Single(i => i.TaskId == 2).IsBlocked);
    }
}
