using System;
using System.Collections.Generic;
using System.Globalization;
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
            row.ErrorMessage = AbandonedMessage(nowUtc - row.StartedAt);
        }

        if (abandoned.Count > 0) await db.SaveChangesAsync(ct);

        return abandoned.Count;
    }

    /// <summary>
    /// Settles every abandoned Running row across every integration, and reports what it settled.
    ///
    /// The kind-scoped <see cref="ReapAbandonedAsync"/> only ever runs when somebody is about to sync
    /// <em>that</em> provider, which is the wrong trigger for the case it exists to handle: a row is
    /// orphaned precisely because its process stopped, and nothing about that makes the next sync
    /// happen. A Vision One row left Running by a killed process sat on the sync-log screen for three
    /// days reading as live work, because the only thing that would have settled it was the next
    /// Vision One sync — and the job host that would have started one was the process that died.
    ///
    /// So this variant takes no kind: the hourly sweep that calls it settles whatever is stale,
    /// whoever left it behind, without needing to know which integrations exist.
    /// </summary>
    /// <returns>
    /// The rows settled, as they were <em>before</em> settling — the caller announces them, and
    /// "which connection, and how long was it stuck" is what makes that announcement worth reading.
    /// </returns>
    public static async Task<IReadOnlyList<AbandonedRun>> ReapAllAsync(AuditableContext db,
        DateTime nowUtc, CancellationToken ct = default)
    {
        var horizon = nowUtc - StaleAfter;

        var abandoned = await db.IntegrationSyncLogs
            .Where(l => l.Status == IntegrationSyncStatus.Running && l.StartedAt < horizon)
            .ToListAsync(ct);

        if (abandoned.Count == 0) return [];

        var reaped = new List<AbandonedRun>(abandoned.Count);

        foreach (var row in abandoned)
        {
            reaped.Add(new AbandonedRun(row.Id, row.Integration, row.ConnectionId, row.ConnectionName,
                row.StartedAt, nowUtc - row.StartedAt));

            row.Status = IntegrationSyncStatus.Failed;
            row.FinishedAt = nowUtc;
            row.ErrorMessage = AbandonedMessage(nowUtc - row.StartedAt);
            row.ProgressLog = IntegrationSyncProgressLog.Append(row.ProgressLog,
            [
                IntegrationSyncProgressLog.Line(nowUtc, "reaped",
                    "No progress was recorded for "
                    + $"{FormatAge(nowUtc - row.StartedAt)}, so the run was settled as failed by the "
                    + "abandoned-run sweep. The process it was running in stopped before it could "
                    + "record an outcome.")
            ]);
        }

        await db.SaveChangesAsync(ct);

        return reaped;
    }

    /// <summary>
    /// What a settled row says about why. Shared by both reapers so the operator reads one
    /// explanation, not two that differ by which code path happened to notice.
    /// </summary>
    internal static string AbandonedMessage(TimeSpan age) =>
        $"The run was abandoned: it was still marked as running {FormatAge(age)} after it started, "
        + "which means the process it was running in stopped before it could record an outcome.";

    /// <summary>
    /// Renders an age the way an operator reads one — hours below a day, days above.
    ///
    /// Invariant culture, because this lands in an error column and a log line: on a pt-BR host the
    /// default formatter writes "3,1 day(s)", which reads as a list of two numbers.
    /// </summary>
    internal static string FormatAge(TimeSpan age) =>
        age.TotalDays >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.#} day(s)", age.TotalDays)
            : string.Format(CultureInfo.InvariantCulture, "{0:0} hour(s)",
                Math.Max(1, Math.Round(age.TotalHours)));

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

/// <summary>
/// One run the sweep found abandoned, described as it was before it was settled.
/// </summary>
/// <param name="LogId">The <c>integration_sync_logs</c> row.</param>
/// <param name="Kind">Which integration left it behind.</param>
/// <param name="ConnectionId">The connection it was syncing.</param>
/// <param name="ConnectionName">That connection's name, as the operator sees it.</param>
/// <param name="StartedAt">When the run started, UTC.</param>
/// <param name="Age">How long it had been Running when the sweep settled it.</param>
public record AbandonedRun(int LogId, IntegrationKind Kind, int? ConnectionId, string? ConnectionName,
    DateTime StartedAt, TimeSpan Age);
