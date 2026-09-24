using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using DAL.Enums;

namespace GUIClient.Tools;

/// <summary>
/// How the Posture providers tab's synchronization log is assembled from the two per-provider
/// server lists plus the run the operator just started.
///
/// The server writes a Running row the moment it claims the connection (see
/// <c>IntegrationSyncLedger</c>), but the client only saw it on the next read — and the only read
/// was the one that follows the sync call, which returns minutes later. So pressing Sync showed
/// nothing in the log until the run was over, and the operator's only way to see that a run existed
/// was to press Refresh by hand. Two things fix that and both live here so they can be tested
/// without Avalonia:
///
///  * <see cref="Pending"/> is the row the client shows for its own run before it has read the
///    server's. It carries <c>Id = 0</c>, which is what marks it as not-yet-persisted.
///  * <see cref="Merge"/> drops that placeholder as soon as the server reports a Running row for the
///    same connection, so the run appears once, not twice.
/// </summary>
public static class IntegrationSyncLogFeed
{
    /// <summary>
    /// The optimistic Running row for a run the client has just requested.
    ///
    /// <paramref name="startedAt"/> is UTC because every persisted <c>StartedAt</c> is, and the
    /// merged list is ordered by that column — a local-time placeholder would sort itself hours out
    /// of place.
    /// </summary>
    public static IntegrationSyncLog Pending(IntegrationKind integration, int connectionId,
        string? connectionName, DateTime startedAt) => new()
    {
        Id = 0,
        Integration = integration,
        ConnectionId = connectionId,
        ConnectionName = connectionName,
        StartedAt = startedAt,
        Status = IntegrationSyncStatus.Running
    };

    /// <summary>
    /// The two provider logs interleaved by start time, newest first, with <paramref name="pending"/>
    /// included only while the server has no Running row of its own for that connection.
    /// </summary>
    public static List<IntegrationSyncLog> Merge(IEnumerable<IntegrationSyncLog>? trendMicro,
        IEnumerable<IntegrationSyncLog>? scorecard, IntegrationSyncLog? pending = null)
    {
        var rows = (trendMicro ?? []).Concat(scorecard ?? []).ToList();

        if (pending != null && !IsClaimed(rows, pending)) rows.Add(pending);

        // Ties broken by id so a placeholder (id 0) never outranks a persisted row it duplicates.
        return rows
            .OrderByDescending(l => l.StartedAt)
            .ThenByDescending(l => l.Id)
            .ToList();
    }

    /// <summary>
    /// The row in <paramref name="rows"/> that continues <paramref name="previous"/>, or null.
    ///
    /// Every refresh replaces the collection with freshly deserialised instances, so the selection
    /// has to be re-established by identity rather than by reference — otherwise the progress panel
    /// blanks on each poll. A placeholder (id 0) is continued by the newest row for the same
    /// connection, which is the server's own row for that same run.
    /// </summary>
    public static IntegrationSyncLog? Reselect(IReadOnlyList<IntegrationSyncLog> rows,
        IntegrationSyncLog? previous)
    {
        if (previous == null) return null;

        if (previous.Id != 0) return rows.FirstOrDefault(r => r.Id == previous.Id);

        return rows.FirstOrDefault(r => r.Integration == previous.Integration
                                        && r.ConnectionId == previous.ConnectionId);
    }

    private static bool IsClaimed(IEnumerable<IntegrationSyncLog> rows, IntegrationSyncLog pending) =>
        rows.Any(r => r.Id != 0
                      && r.Integration == pending.Integration
                      && r.ConnectionId == pending.ConnectionId
                      && r.Status == IntegrationSyncStatus.Running);
}
