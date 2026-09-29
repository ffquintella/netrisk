using System.Collections.Generic;
using System.Linq;
using Model.Integrations;

namespace GUIClient.Tools;

/// <summary>One row of the queue grid, reduced to what the save needs.</summary>
public readonly record struct QueueRow(int QueueId, string Name, bool Import, int MaxRequests);

/// <summary>
/// The queue selection the Service Management Save sends. Free of Avalonia so
/// <c>GUIClient.Tests</c> can compile it directly.
/// </summary>
public static class JiraQueueImports
{
    /// <summary>
    /// <c>null</c> until the grid has been loaded, which the server reads as "keep the stored
    /// selection": the grid starts empty, and projecting it would delete every stored import.
    /// </summary>
    public static List<JiraQueueImportView>? ForSave(bool queuesLoaded, int serviceDeskId,
        IEnumerable<QueueRow> rows)
    {
        if (!queuesLoaded) return null;

        return rows.Where(q => q.Import)
            .Select(q => new JiraQueueImportView
            {
                ServiceDeskId = serviceDeskId,
                QueueId = q.QueueId,
                QueueName = q.Name,
                Enabled = true,
                MaxRequests = q.MaxRequests
            }).ToList();
    }
}
