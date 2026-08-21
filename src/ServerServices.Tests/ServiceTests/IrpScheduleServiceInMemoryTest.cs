using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Critical-path scheduling for incident response plans (Track 2 milestone 2.4.3).
/// </summary>
public class IrpScheduleServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIrpScheduleService _svc;

    private static readonly DateTime PlanStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public IrpScheduleServiceInMemoryTest() => _svc = GetService<IIrpScheduleService>();

    private void SeedPlan(params IncidentResponsePlanTask[] tasks)
    {
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan
            {
                Id = 1,
                Name = "Ransomware response",
                Description = "d",
                CreationDate = PlanStart,
                LastUpdate = PlanStart,
                CreatedById = 1
            });

            foreach (var task in tasks)
            {
                task.PlanId = 1;
                ctx.IncidentResponsePlanTasks.Add(task);
            }
        });
    }

    private static IncidentResponsePlanTask Task(
        int id, string name, int order, double hours, bool sequential = false, int status = 0) => new()
    {
        Id = id,
        Name = name,
        ExecutionOrder = order,
        EstimatedDuration = TimeSpan.FromHours(hours),
        IsSequential = sequential,
        AssignedToId = 1,
        Status = status,
        CreationDate = PlanStart
    };

    [Fact]
    public async Task ReturnsNullForUnknownPlan()
    {
        Assert.Null(await _svc.GetScheduleAsync(999));
    }

    [Fact]
    public async Task EmptyPlanSchedulesToItsStart()
    {
        SeedPlan();

        var schedule = await _svc.GetScheduleAsync(1);

        Assert.NotNull(schedule);
        Assert.Empty(schedule!.Items);
        Assert.Equal(PlanStart, schedule.PlanEnd);
        Assert.Empty(schedule.CriticalPath);
    }

    [Fact]
    public async Task ParallelTasksInOneStageShareAStart()
    {
        SeedPlan(
            Task(1, "Contain", 1, 2),
            Task(2, "Notify", 1, 1));

        var schedule = await _svc.GetScheduleAsync(1);
        var contain = schedule!.Items.Single(i => i.TaskId == 1);
        var notify = schedule.Items.Single(i => i.TaskId == 2);

        Assert.Equal(TimeSpan.Zero, contain.EarlyStart);
        Assert.Equal(TimeSpan.Zero, notify.EarlyStart);

        // The stage lasts as long as its longest task.
        Assert.Equal(TimeSpan.FromHours(2), schedule.TotalDuration);
    }

    [Fact]
    public async Task LaterStageWaitsForTheWholePreviousStage()
    {
        SeedPlan(
            Task(1, "Contain", 1, 2),
            Task(2, "Notify", 1, 1),
            Task(3, "Eradicate", 2, 3));

        var schedule = await _svc.GetScheduleAsync(1);
        var eradicate = schedule!.Items.Single(i => i.TaskId == 3);

        Assert.Equal(TimeSpan.FromHours(2), eradicate.EarlyStart);
        Assert.Equal(TimeSpan.FromHours(5), schedule.TotalDuration);
        Assert.Equal(PlanStart.AddHours(5), schedule.PlanEnd);
    }

    [Fact]
    public async Task CriticalPathIsTheLongestChainAndSlackIsZeroOnIt()
    {
        SeedPlan(
            Task(1, "Contain", 1, 2),
            Task(2, "Notify", 1, 1),
            Task(3, "Eradicate", 2, 3));

        var schedule = await _svc.GetScheduleAsync(1);

        var contain = schedule!.Items.Single(i => i.TaskId == 1);
        var notify = schedule.Items.Single(i => i.TaskId == 2);
        var eradicate = schedule.Items.Single(i => i.TaskId == 3);

        Assert.True(contain.IsCritical);
        Assert.True(eradicate.IsCritical);

        // Notify is an hour shorter than the stage, so it may slip an hour without moving the end.
        Assert.False(notify.IsCritical);
        Assert.Equal(TimeSpan.FromHours(1), notify.Slack);

        Assert.Equal(new[] { 1, 3 }, schedule.CriticalPath);
    }

    [Fact]
    public async Task SequentialTasksInOneStageChain()
    {
        SeedPlan(
            Task(1, "Step one", 1, 2, sequential: true),
            Task(2, "Step two", 1, 3, sequential: true));

        var schedule = await _svc.GetScheduleAsync(1);
        var second = schedule!.Items.Single(i => i.TaskId == 2);

        Assert.Contains(1, second.DependsOn);
        Assert.Equal(TimeSpan.FromHours(2), second.EarlyStart);
        Assert.Equal(TimeSpan.FromHours(5), schedule.TotalDuration);
    }

    [Fact]
    public async Task TaskWithNoEstimateGetsTheNominalDuration()
    {
        Seed(ctx =>
        {
            ctx.IncidentResponsePlans.Add(new IncidentResponsePlan
            {
                Id = 1, Name = "P", Description = "d",
                CreationDate = PlanStart, LastUpdate = PlanStart, CreatedById = 1
            });
            ctx.IncidentResponsePlanTasks.Add(new IncidentResponsePlanTask
            {
                Id = 1, PlanId = 1, Name = "No estimate", ExecutionOrder = 1,
                EstimatedDuration = null, AssignedToId = 1, CreationDate = PlanStart
            });
        });

        var schedule = await _svc.GetScheduleAsync(1);

        Assert.Equal(TimeSpan.FromHours(1), schedule!.Items.Single().Duration);
    }

    [Fact]
    public async Task BlockedIsTrueWhileAPredecessorIsIncomplete()
    {
        SeedPlan(
            Task(1, "Contain", 1, 2, status: (int)IntStatus.New),
            Task(2, "Eradicate", 2, 1));

        var schedule = await _svc.GetScheduleAsync(1);

        Assert.True(schedule!.Items.Single(i => i.TaskId == 2).IsBlocked);
        Assert.False(schedule.Items.Single(i => i.TaskId == 1).IsBlocked);
    }

    [Fact]
    public async Task CompletingThePredecessorUnblocksTheSuccessor()
    {
        SeedPlan(
            Task(1, "Contain", 1, 2, status: (int)IntStatus.Closed),
            Task(2, "Eradicate", 2, 1));

        var schedule = await _svc.GetScheduleAsync(1);

        Assert.False(schedule!.Items.Single(i => i.TaskId == 2).IsBlocked);
    }

    [Fact]
    public async Task OpenTaskWhoseWindowHasPassedIsOverdue()
    {
        // The plan is anchored in 2026-01, so every bar ends in the past relative to "now".
        SeedPlan(
            Task(1, "Contain", 1, 2, status: (int)IntStatus.New),
            Task(2, "Done already", 1, 1, status: (int)IntStatus.Closed));

        var schedule = await _svc.GetScheduleAsync(1);

        Assert.True(schedule!.Items.Single(i => i.TaskId == 1).IsOverdue);

        // A completed task is never overdue, however long ago its window closed.
        Assert.False(schedule.Items.Single(i => i.TaskId == 2).IsOverdue);
    }

    [Fact]
    public async Task BranchingPlanCriticalPathMatchesTheManualCalculation()
    {
        // Stage 1: A(1h) B(4h)  → stage ends at 4h
        // Stage 2: C(2h) D(1h)  → stage ends at 6h
        // Stage 3: E(3h)        → plan ends at 9h; the chain through B and C is the longest.
        SeedPlan(
            Task(1, "A", 1, 1),
            Task(2, "B", 1, 4),
            Task(3, "C", 2, 2),
            Task(4, "D", 2, 1),
            Task(5, "E", 3, 3));

        var schedule = await _svc.GetScheduleAsync(1);

        Assert.Equal(TimeSpan.FromHours(9), schedule!.TotalDuration);
        Assert.Equal(new[] { 2, 3, 5 }, schedule.CriticalPath);

        Assert.Equal(TimeSpan.FromHours(3), schedule.Items.Single(i => i.TaskId == 1).Slack);
        Assert.Equal(TimeSpan.FromHours(1), schedule.Items.Single(i => i.TaskId == 4).Slack);
    }
}
