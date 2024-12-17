using DAL.Entities;

namespace Tools.IncidentResponsePlans;

public static class TaskSorter
{
    public async static Task<List<IncidentResponsePlanTask>> SortTasksAsync(List<IncidentResponsePlanTask> tasks)
    {
        return await tasks.AsParallel().ToAsyncEnumerable()
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.IsOptional)
            .ThenByDescending(t => t.IsSequential)
            .ThenBy(t => t.IsParallel)
            .ThenBy(t => t.Name).ToListAsync();
    }
}