using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.IncidentResponsePlan;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

/// <summary>
/// Computes the incident-response Gantt and its critical path (Track 2 milestone 2.4.3).
///
/// Plan tasks carry no explicit <c>depends_on</c> edges; the ordering they do carry is
/// <c>ExecutionOrder</c> (the stage) plus the <c>IsSequential</c> flag. The graph is therefore
/// derived: every task waits on the whole of the preceding stage, and sequential tasks inside a
/// stage additionally chain to each other. That is exactly the ordering the plan editor lets an
/// author express, so the bars match the plan as written rather than inventing dependencies.
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

        var items = BuildItems(tasks);
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
    private static List<IrpScheduleItem> BuildItems(List<IncidentResponsePlanTask> tasks)
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
}
