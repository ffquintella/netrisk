using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Exceptions;
using Model.IncidentResponsePlan;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

/// <summary>
/// Computes the incident-response Gantt and its critical path (Track 2 milestone 2.4.3).
///
/// Dependencies come from two places. Explicit edges stored in
/// <c>incident_response_plan_task_dependencies</c> win: an author who has said "this task waits on
/// that one" means exactly that. A task with no explicit edge falls back to the ordering the plan
/// already carried before those edges existed — <c>ExecutionOrder</c> as a stage, plus the
/// <c>IsSequential</c> flag chaining within a stage — so plans authored before this feature still
/// schedule the way they always did instead of collapsing into one parallel block.
///
/// With the graph in hand this is a textbook CPM forward/backward pass: slack of zero means the
/// task cannot slip without moving the plan's end date, which is the definition of critical.
/// </summary>
public class IrpScheduleService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IIrpScheduleService
{
    /// <summary>
    /// Duration assumed for a task with no estimate. Zero would make every unestimated task a
    /// milestone and collapse the chart, so they get a nominal hour and are visibly short.
    /// </summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

    private static readonly HashSet<int> CompletedStatuses =
    [
        (int)IntStatus.Closed,
        (int)IntStatus.Completed,
        (int)IntStatus.Done,
        (int)IntStatus.Solved,
        (int)IntStatus.Skipped,
        (int)IntStatus.Cancelled
    ];

    public async Task<IrpSchedule?> GetScheduleAsync(int planId)
    {
        await using var dbContext = DalService.GetContext();

        var plan = await dbContext.IncidentResponsePlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == planId);

        if (plan == null)
        {
            Logger.Warning("Schedule requested for unknown incident response plan {PlanId}", planId);
            return null;
        }

        var tasks = await dbContext.IncidentResponsePlanTasks
            .AsNoTracking()
            .Where(t => t.PlanId == planId)
            .OrderBy(t => t.ExecutionOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var taskIds = tasks.Select(t => t.Id).ToList();

        var explicitEdges = await dbContext.IncidentResponsePlanTaskDependencies
            .AsNoTracking()
            .Where(d => taskIds.Contains(d.TaskId))
            .Select(d => new { d.TaskId, d.DependsOnTaskId })
            .ToListAsync();

        var edgesByTask = explicitEdges
            .GroupBy(e => e.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.DependsOnTaskId).ToList());

        var schedule = new IrpSchedule
        {
            PlanId = plan.Id,
            PlanName = plan.Name,
            PlanStart = plan.CreationDate
        };

        if (tasks.Count == 0)
        {
            schedule.PlanEnd = plan.CreationDate;
            return schedule;
        }

        var items = BuildItems(tasks, edgesByTask);
        var byId = items.ToDictionary(i => i.TaskId);

        ForwardPass(items, byId);

        var total = items.Max(i => i.EarlyFinish);

        BackwardPass(items, byId, total);
        ApplyWallClock(items, plan.CreationDate, tasks);

        schedule.Items = items;
        schedule.TotalDuration = total;
        schedule.PlanEnd = plan.CreationDate + total;
        schedule.CriticalPath = ExtractCriticalPath(items, byId);

        Logger.Information(
            "Scheduled incident response plan {PlanId}: {TaskCount} tasks, critical path {PathLength} tasks, {Duration}",
            planId, items.Count, schedule.CriticalPath.Count, total);

        return schedule;
    }

    /// <summary>
    /// Turns the flat task list into schedule items carrying a derived dependency set.
    /// </summary>
    private static List<IrpScheduleItem> BuildItems(
        List<IncidentResponsePlanTask> tasks,
        IReadOnlyDictionary<int, List<int>> explicitEdges)
    {
        var items = tasks.Select(t => new IrpScheduleItem
        {
            TaskId = t.Id,
            Name = t.Name,
            ExecutionOrder = t.ExecutionOrder,
            Status = t.Status,
            AssignedToId = t.AssignedToId,
            Duration = t.EstimatedDuration is { Ticks: > 0 } d ? d : DefaultDuration
        }).ToList();

        var stages = tasks
            .GroupBy(t => t.ExecutionOrder)
            .OrderBy(g => g.Key)
            .ToList();

        List<int>? previousStageIds = null;

        foreach (var stage in stages)
        {
            var stageTasks = stage.OrderBy(t => t.Id).ToList();

            // Sequential tasks inside a stage form a chain in id order; everything else in the
            // stage runs in parallel off the previous stage.
            int? previousSequentialId = null;

            foreach (var task in stageTasks)
            {
                var item = items.First(i => i.TaskId == task.Id);

                // An explicit edge is a statement of intent and replaces the stage ordering for
                // this task; only tasks nobody has spoken for fall back to it.
                if (explicitEdges.TryGetValue(task.Id, out var declared) && declared.Count > 0)
                {
                    // Ignore an edge pointing outside this plan; a cross-plan edge cannot be
                    // scheduled here and the acyclicity check already refuses to create one.
                    item.DependsOn.AddRange(declared.Where(id => items.Any(i => i.TaskId == id)));

                    if (task.IsSequential == true) previousSequentialId = task.Id;
                    continue;
                }

                if (previousStageIds != null)
                {
                    item.DependsOn.AddRange(previousStageIds);
                }

                if (task.IsSequential == true && previousSequentialId.HasValue)
                {
                    item.DependsOn.Add(previousSequentialId.Value);
                }

                if (task.IsSequential == true)
                {
                    previousSequentialId = task.Id;
                }
            }

            previousStageIds = stageTasks.Select(t => t.Id).ToList();
        }

        return items;
    }

    /// <summary>Early start/finish, walking the tasks in the topological order the stages give.</summary>
    private static void ForwardPass(List<IrpScheduleItem> items, Dictionary<int, IrpScheduleItem> byId)
    {
        foreach (var item in items.OrderBy(i => i.ExecutionOrder).ThenBy(i => i.TaskId))
        {
            var earliest = TimeSpan.Zero;

            foreach (var dependencyId in item.DependsOn)
            {
                if (byId.TryGetValue(dependencyId, out var dependency) && dependency.EarlyFinish > earliest)
                {
                    earliest = dependency.EarlyFinish;
                }
            }

            item.EarlyStart = earliest;
            item.EarlyFinish = earliest + item.Duration;
        }
    }

    /// <summary>Late start/finish and slack, walking the same order in reverse.</summary>
    private static void BackwardPass(
        List<IrpScheduleItem> items, Dictionary<int, IrpScheduleItem> byId, TimeSpan total)
    {
        // successors[x] = the tasks waiting on x
        var successors = new Dictionary<int, List<int>>();
        foreach (var item in items)
        {
            foreach (var dependencyId in item.DependsOn)
            {
                if (!successors.TryGetValue(dependencyId, out var list))
                {
                    list = [];
                    successors[dependencyId] = list;
                }
                list.Add(item.TaskId);
            }
        }

        foreach (var item in items.OrderByDescending(i => i.ExecutionOrder).ThenByDescending(i => i.TaskId))
        {
            // A task nothing waits on only has to finish by the plan's end.
            var latestFinish = total;

            if (successors.TryGetValue(item.TaskId, out var itemSuccessors))
            {
                foreach (var successorId in itemSuccessors)
                {
                    if (byId.TryGetValue(successorId, out var successor) && successor.LateStart < latestFinish)
                    {
                        latestFinish = successor.LateStart;
                    }
                }
            }

            item.LateFinish = latestFinish;
            item.LateStart = latestFinish - item.Duration;
            item.Slack = item.LateStart - item.EarlyStart;
            item.IsCritical = item.Slack <= TimeSpan.Zero;
        }
    }

    private static void ApplyWallClock(
        List<IrpScheduleItem> items, DateTime planStart, List<IncidentResponsePlanTask> tasks)
    {
        var statusById = tasks.ToDictionary(t => t.Id, t => t.Status);
        var now = DateTime.UtcNow;

        foreach (var item in items)
        {
            item.StartDate = planStart + item.EarlyStart;
            item.EndDate = planStart + item.EarlyFinish;

            var isComplete = CompletedStatuses.Contains(item.Status);

            item.IsOverdue = !isComplete && item.EndDate < now;

            item.IsBlocked = item.DependsOn.Any(dependencyId =>
                statusById.TryGetValue(dependencyId, out var dependencyStatus) &&
                !CompletedStatuses.Contains(dependencyStatus));
        }
    }

    /// <summary>
    /// The critical path as an ordered chain: start at the latest-finishing critical task and
    /// walk back through whichever critical predecessor it actually waits on.
    /// </summary>
    private static List<int> ExtractCriticalPath(
        List<IrpScheduleItem> items, Dictionary<int, IrpScheduleItem> byId)
    {
        var critical = items.Where(i => i.IsCritical).ToList();
        if (critical.Count == 0) return [];

        var chain = new List<int>();
        var cursor = critical.OrderByDescending(i => i.EarlyFinish).ThenByDescending(i => i.TaskId).First();
        var guard = new HashSet<int>();

        while (cursor != null && guard.Add(cursor.TaskId))
        {
            chain.Add(cursor.TaskId);

            cursor = cursor.DependsOn
                .Select(id => byId.GetValueOrDefault(id))
                .Where(d => d is { IsCritical: true })
                .OrderByDescending(d => d!.EarlyFinish)
                .FirstOrDefault();
        }

        chain.Reverse();
        return chain;
    }

    public async Task<List<IrpTaskDependency>> GetDependenciesAsync(int planId)
    {
        await using var dbContext = DalService.GetContext();

        var taskNames = await dbContext.IncidentResponsePlanTasks
            .AsNoTracking()
            .Where(t => t.PlanId == planId)
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        var taskIds = taskNames.Keys.ToList();

        var edges = await dbContext.IncidentResponsePlanTaskDependencies
            .AsNoTracking()
            .Where(d => taskIds.Contains(d.TaskId))
            .ToListAsync();

        return edges.Select(d => new IrpTaskDependency
        {
            Id = d.Id,
            TaskId = d.TaskId,
            TaskName = taskNames.GetValueOrDefault(d.TaskId) ?? $"#{d.TaskId}",
            DependsOnTaskId = d.DependsOnTaskId,
            DependsOnTaskName = taskNames.GetValueOrDefault(d.DependsOnTaskId) ?? $"#{d.DependsOnTaskId}",
            CreatedAt = d.CreatedAt
        }).ToList();
    }

    public async Task<IrpTaskDependency> AddDependencyAsync(int planId, int taskId, int dependsOnTaskId)
    {
        if (taskId == dependsOnTaskId)
            throw new RuleBrokenException("A task cannot depend on itself", "DependsOnTaskId");

        await using var dbContext = DalService.GetContext();

        var tasks = await dbContext.IncidentResponsePlanTasks
            .AsNoTracking()
            .Where(t => t.PlanId == planId)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync();

        var task = tasks.FirstOrDefault(t => t.Id == taskId);
        var dependency = tasks.FirstOrDefault(t => t.Id == dependsOnTaskId);

        // Both ends must belong to this plan — an edge across plans could never be scheduled.
        if (task == null)
            throw new DataNotFoundException("IncidentResponsePlanTask", taskId.ToString());

        if (dependency == null)
            throw new RuleBrokenException(
                $"Task {dependsOnTaskId} does not belong to plan {planId}", "DependsOnTaskId");

        var taskIds = tasks.Select(t => t.Id).ToList();
        var existing = await dbContext.IncidentResponsePlanTaskDependencies
            .AsNoTracking()
            .Where(d => taskIds.Contains(d.TaskId))
            .Select(d => new { d.TaskId, d.DependsOnTaskId })
            .ToListAsync();

        if (existing.Any(e => e.TaskId == taskId && e.DependsOnTaskId == dependsOnTaskId))
        {
            // Already declared. Return the stored edge rather than tripping the unique index.
            var stored = await dbContext.IncidentResponsePlanTaskDependencies
                .AsNoTracking()
                .FirstAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId);

            return ToDto(stored, tasks.ToDictionary(t => t.Id, t => t.Name));
        }

        var adjacency = existing
            .GroupBy(e => e.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.DependsOnTaskId).ToList());

        if (WouldCloseACycle(adjacency, taskId, dependsOnTaskId))
        {
            throw new RuleBrokenException(
                $"'{task.Name}' cannot wait on '{dependency.Name}': that would create a dependency cycle",
                "DependsOnTaskId");
        }

        var edge = new IncidentResponsePlanTaskDependency
        {
            TaskId = taskId,
            DependsOnTaskId = dependsOnTaskId,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.IncidentResponsePlanTaskDependencies.Add(edge);
        await dbContext.SaveChangesAsync();

        Logger.Information("Task {TaskId} of plan {PlanId} now waits on task {DependsOnTaskId}",
            taskId, planId, dependsOnTaskId);

        return ToDto(edge, tasks.ToDictionary(t => t.Id, t => t.Name));
    }

    public async Task RemoveDependencyAsync(int planId, int taskId, int dependsOnTaskId)
    {
        await using var dbContext = DalService.GetContext();

        var edge = await dbContext.IncidentResponsePlanTaskDependencies
            .FirstOrDefaultAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId);

        if (edge == null) return;

        dbContext.IncidentResponsePlanTaskDependencies.Remove(edge);
        await dbContext.SaveChangesAsync();

        Logger.Information("Task {TaskId} of plan {PlanId} no longer waits on task {DependsOnTaskId}",
            taskId, planId, dependsOnTaskId);
    }

    public async Task<IrpScheduleItem> CompleteBlockedTaskAsync(int planId, int taskId, int userId, string reason)
    {
        // The whole point of the gate is that the reason is recorded, so an empty one defeats it.
        if (string.IsNullOrWhiteSpace(reason))
            throw new RuleBrokenException("An override reason is required to complete a blocked task", "Reason");

        var schedule = await GetScheduleAsync(planId)
                       ?? throw new DataNotFoundException("IncidentResponsePlan", planId.ToString());

        var item = schedule.Items.FirstOrDefault(i => i.TaskId == taskId)
                   ?? throw new DataNotFoundException("IncidentResponsePlanTask", taskId.ToString());

        await using var dbContext = DalService.GetContext();

        var task = await dbContext.IncidentResponsePlanTasks
                       .FirstOrDefaultAsync(t => t.Id == taskId && t.PlanId == planId)
                   ?? throw new DataNotFoundException("IncidentResponsePlanTask", taskId.ToString());

        task.Status = (int)IntStatus.Closed;
        task.LastUpdate = DateTime.UtcNow;
        task.UpdatedById = userId;

        // Only stamp the override when the task really was blocked. Recording one on a task that
        // was free to complete would make the audit trail read as though a rule had been bent.
        if (item.IsBlocked)
        {
            task.OverrideReason = reason;
            task.OverriddenById = userId;
            task.OverriddenAt = DateTime.UtcNow;

            Logger.Warning(
                "User {UserId} completed blocked task {TaskId} of plan {PlanId} by override: {Reason}",
                userId, taskId, planId, reason);
        }

        await dbContext.SaveChangesAsync();

        var refreshed = await GetScheduleAsync(planId);
        return refreshed!.Items.First(i => i.TaskId == taskId);
    }

    private static IrpTaskDependency ToDto(
        IncidentResponsePlanTaskDependency edge, IReadOnlyDictionary<int, string> names) => new()
    {
        Id = edge.Id,
        TaskId = edge.TaskId,
        TaskName = names.GetValueOrDefault(edge.TaskId) ?? $"#{edge.TaskId}",
        DependsOnTaskId = edge.DependsOnTaskId,
        DependsOnTaskName = names.GetValueOrDefault(edge.DependsOnTaskId) ?? $"#{edge.DependsOnTaskId}",
        CreatedAt = edge.CreatedAt
    };

    /// <summary>
    /// Walks the existing edges up from the proposed predecessor: if the task being edited is
    /// reachable, adding the edge closes a loop.
    /// </summary>
    private static bool WouldCloseACycle(
        IReadOnlyDictionary<int, List<int>> adjacency, int taskId, int dependsOnTaskId)
    {
        var stack = new Stack<int>();
        var seen = new HashSet<int>();
        stack.Push(dependsOnTaskId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current == taskId) return true;

            // Also stops a cycle that somehow already exists from spinning forever.
            if (!seen.Add(current)) continue;

            if (!adjacency.TryGetValue(current, out var predecessors)) continue;

            foreach (var predecessor in predecessors) stack.Push(predecessor);
        }

        return false;
    }
}
