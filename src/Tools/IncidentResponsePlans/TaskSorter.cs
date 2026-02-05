using DAL.Entities;

namespace Tools.IncidentResponsePlans;

public static class TaskSorter
{
    public static Task<List<IncidentResponsePlanTask>> SortTasksAsync(List<IncidentResponsePlanTask> tasks)
    {
        var result = tasks
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.IsOptional)
            .ThenByDescending(t => t.IsSequential)
            .ThenBy(t => t.IsParallel)
            .ThenBy(t => t.Name)
            .ToList();
        return Task.FromResult(result);
    }
}
