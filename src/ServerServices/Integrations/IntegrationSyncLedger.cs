using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;

namespace ServerServices.Integrations;

/// <summary>
/// The single-flight discipline over the shared <c>integration_sync_logs</c> ledger (Track 4).
///
/// Two properties, both learned from a sync-log screen that showed eleven Vision One runs "Running"
/// within three seconds of each other for a connection that has exactly one:
///
///  * <b>One run per connection at a time.</b> Nothing upstream of the service guarantees this. The
///    manual sync is a POST, and a POST is retried by the desktop client's reliable REST wrapper on
///    every 5xx — with no delay between attempts — so a single click could start a dozen concurrent
///    syncs, each racing the others to write the same hosts and findings. The daily job can collide
///    with a manual run for the same reason. <see cref="ClaimAsync"/> is what makes the second one
///    refuse instead of run.
///  * <b>A Running row is never permanent.</b> A process restarted mid-sync, or a completion write
///    that itself failed, leaves a row that says Running forever and reads exactly like a sync still
///    in progress. Left alone that also wedges the guard above. <see cref="ReapAbandonedAsync"/>
///    settles anything older than <see cref="StaleAfter"/> before the guard is consulted, so a stuck
///    row costs one refused sync at most.
///
/// The claim is insert-then-check rather than check-then-insert: two callers that read the ledger at
/// the same instant both see no run in flight, and only a claim that can lose *after* writing its own
/// row resolves that race. The row with the lowest id wins, which is also the one that started first.
/// </summary>
public static class IntegrationSyncLedger
{
    /// <summary>
    /// How long a Running row is believed before it is treated as abandoned.
    ///
    /// Two hours is above any real posture sync (the largest tenant tested runs in minutes) and well
    /// below the daily job's cadence, so it cannot mistake a slow run for a dead one, and a genuinely
    /// dead one does not block the operator until tomorrow.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    /// <summary>
    /// Settles every Running row for <paramref name="kind"/> that started more than
    /// <see cref="StaleAfter"/> ago, optionally narrowed to one connection. Returns how many were
    /// settled.
    /// </summary>
    public static async Task<int> ReapAbandonedAsync(AuditableContext db, IntegrationKind kind,
        DateTime nowUtc, int? connectionId = null, CancellationToken ct = default)
    {
        var horizon = nowUtc - StaleAfter;

        var abandoned = await db.IntegrationSyncLogs
            .Where(l => l.Integration == kind
                        && l.Status == IntegrationSyncStatus.Running
                        && l.StartedAt < horizon
                        && (connectionId == null || l.ConnectionId == connectionId))
            .ToListAsync(ct);

        foreach (var row in abandoned)
        {
            row.Status = IntegrationSyncStatus.Failed;
            row.FinishedAt = nowUtc;
            row.ErrorMessage = $"The run was abandoned: it was still marked as running "
                               + $"{StaleAfter.TotalHours:0} hours after it started, which means the "
                               + "process it was running in stopped before it could record an outcome.";
        }

        if (abandoned.Count > 0) await db.SaveChangesAsync(ct);

        return abandoned.Count;
    }

    /// <summary>
    /// Claims the connection for a new run and returns its Running row.
    /// </summary>
    /// <exception cref="IntegrationSyncBusyException">
    /// A run for this connection is already in flight. Nothing is left behind in the ledger when this
    /// is thrown — a refused duplicate is not a run, and eleven rows saying so is the screen this
    /// exists to prevent.
    /// </exception>
    public static async Task<IntegrationSyncLog> ClaimAsync(AuditableContext db, IntegrationKind kind,
        int connectionId, string connectionName, string provider, DateTime nowUtc,
        CancellationToken ct = default)
    {
        await ReapAbandonedAsync(db, kind, nowUtc, connectionId, ct);

        var claim = new IntegrationSyncLog
        {
            Integration = kind,
            ConnectionId = connectionId,
            ConnectionName = connectionName,
            StartedAt = nowUtc,
            Status = IntegrationSyncStatus.Running
        };

        db.IntegrationSyncLogs.Add(claim);
        await db.SaveChangesAsync(ct);

        var incumbent = await db.IntegrationSyncLogs
            .Where(l => l.Integration == kind
                        && l.ConnectionId == connectionId
                        && l.Status == IntegrationSyncStatus.Running
                        && l.Id < claim.Id)
            .OrderBy(l => l.Id)
            .FirstOrDefaultAsync(ct);

        if (incumbent == null) return claim;

        db.IntegrationSyncLogs.Remove(claim);
        await db.SaveChangesAsync(ct);

        throw new IntegrationSyncBusyException(provider, connectionName, incumbent.StartedAt);
    }
}
