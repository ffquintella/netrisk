using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ServerServices.Services;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using ILogger = Serilog.ILogger;

namespace ServerServices.Integrations;

/// <summary>
/// Opens a tracked integration sync run (Track 4).
///
/// Every integration that syncs gets its run through here, so that the progress trail and the
/// start/finish notifications exist once rather than five times. Before this, the two posture
/// integrations claimed a ledger row and the three Jira/issue-tracker paths inserted a finished row
/// after the fact — which meant a sync in progress was indistinguishable from no sync at all for three
/// of the five, and invisible in the GUI for all five until it was over.
/// </summary>
public interface IIntegrationSyncTracker
{
    /// <summary>
    /// Writes the run's <see cref="IntegrationSyncStatus.Running"/> row, announces the start, and
    /// returns the handle the run reports progress through.
    /// </summary>
    /// <param name="singleFlight">
    /// True to refuse when a run for this connection is already in flight, via
    /// <see cref="IntegrationSyncLedger.ClaimAsync"/>. Per-integration rather than always-on because
    /// turning it on where it was off is a behaviour change: it makes a concurrent second run throw
    /// <see cref="Model.Exceptions.IntegrationSyncBusyException"/> where today it runs. The two posture
    /// integrations pass true because they already claimed; the issue-tracker and Jira paths pass false
    /// so that adding a progress trail does not also change when their syncs are allowed to run.
    /// </param>
    /// <exception cref="Model.Exceptions.IntegrationSyncBusyException">
    /// <paramref name="singleFlight"/> is true and a run for this connection is already in flight.
    /// </exception>
    Task<IntegrationSyncRun> BeginAsync(IntegrationKind kind, int connectionId, string connectionName,
        string providerLabel, bool singleFlight = true, CancellationToken ct = default);
}

/// <inheritdoc />
public class IntegrationSyncTracker(
    ILogger logger,
    IDalService dalService,
    IIntegrationSyncNotifier notifier)
    : IIntegrationSyncTracker
{
    public async Task<IntegrationSyncRun> BeginAsync(IntegrationKind kind, int connectionId,
        string connectionName, string providerLabel, bool singleFlight = true,
        CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;

        IntegrationSyncLog row;

        await using (var db = dalService.GetContext())
        {
            if (singleFlight)
            {
                // Throws IntegrationSyncBusyException and leaves nothing behind when it loses — see
                // IntegrationSyncLedger for why the claim is insert-then-check.
                row = await IntegrationSyncLedger.ClaimAsync(db, kind, connectionId, connectionName,
                    providerLabel, startedAt, ct);
            }
            else
            {
                // Also reaped, even without the guard. Nothing consults a Running row here, but a row
                // stuck Running forever still misreports on the sync-log screen as a run in progress.
                await IntegrationSyncLedger.ReapAbandonedAsync(db, kind, startedAt, connectionId, ct);

                row = new IntegrationSyncLog
                {
                    Integration = kind,
                    ConnectionId = connectionId,
                    ConnectionName = connectionName,
                    StartedAt = startedAt,
                    Status = IntegrationSyncStatus.Running
                };

                db.IntegrationSyncLogs.Add(row);
                await db.SaveChangesAsync(ct);
            }
        }

        var run = new IntegrationSyncRun(logger, dalService, notifier, row.Id, kind, connectionId,
            connectionName, startedAt);

        await run.StepAsync("started", $"{providerLabel} synchronization started.", ct: ct);

        // After the Running row exists and after the first progress line, so that a GUI woken by the
        // notification finds a run to look at rather than nothing.
        await notifier.StartedAsync(kind, connectionName, ct);

        return run;
    }
}

/// <summary>
/// One in-flight integration sync run: its ledger row, its progress trail, and its notifications.
///
/// Progress is buffered and flushed in batches rather than written per step. A row rewrite per step
/// puts one UPDATE behind every asset — a few thousand of them for a real tenant, against a
/// <c>longtext</c> column that grows as it goes, which is how a diagnostic aid becomes the slowest part
/// of the sync it is diagnosing.
/// </summary>
public sealed class IntegrationSyncRun : IAsyncDisposable
{
    /// <summary>Pending lines that force a flush, whatever the clock says.</summary>
    private const int FlushAfterLines = 25;

    /// <summary>
    /// How stale the persisted trail may get while a run is going.
    ///
    /// Five seconds is under the desktop client's ten-second notification poll, so a trail the operator
    /// opens is never more than one poll behind the run producing it.
    /// </summary>
    private static readonly TimeSpan FlushAfter = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger;
    private readonly IDalService _dalService;
    private readonly IIntegrationSyncNotifier _notifier;

    private readonly List<string> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _bufferLock = new();

    private DateTime _lastFlush;
    private bool _completed;

    internal IntegrationSyncRun(ILogger logger, IDalService dalService, IIntegrationSyncNotifier notifier,
        int logId, IntegrationKind kind, int connectionId, string connectionName, DateTime startedAt)
    {
        _logger = logger;
        _dalService = dalService;
        _notifier = notifier;

        LogId = logId;
        Kind = kind;
        ConnectionId = connectionId;
        ConnectionName = connectionName;
        StartedAt = startedAt;

        _lastFlush = startedAt;
    }

    /// <summary>Id of this run's <c>integration_sync_logs</c> row.</summary>
    public int LogId { get; }

    public IntegrationKind Kind { get; }

    public int ConnectionId { get; }

    public string ConnectionName { get; }

    public DateTime StartedAt { get; }

    /// <summary>
    /// Records a step, and persists the trail when it is due.
    ///
    /// Never throws. A progress line that could not be recorded must not fail the sync it is describing
    /// — the trail exists to explain a sync, so a trail that can break one is worse than no trail.
    /// </summary>
    /// <param name="step">Short step name, e.g. <c>inventory</c> or <c>cves</c>.</param>
    /// <param name="message">What happened.</param>
    /// <param name="processed">Item count, when the step counts items.</param>
    public async Task StepAsync(string step, string message, int? processed = null,
        CancellationToken ct = default)
    {
        var line = IntegrationSyncProgressLog.Line(DateTime.UtcNow, step, message, processed);

        bool due;

        lock (_bufferLock)
        {
            _pending.Add(line);
            due = _pending.Count >= FlushAfterLines || DateTime.UtcNow - _lastFlush >= FlushAfter;
        }

        // Also to Serilog at Debug. The trail is the operator's view; the process log is the
        // developer's, and a run whose database write is the thing that broke still has to be
        // explainable from somewhere.
        _logger.Debug("Integration sync {Kind}/{Connection} [{Log}]: {Line}", Kind, ConnectionName,
            LogId, line);

        if (due) await FlushAsync(ct);
    }

    /// <summary>
    /// Records a step without persisting anything.
    ///
    /// For a caller that is about to write the row itself and will pick the line up through
    /// <see cref="DrainInto"/> — the posture integrations' completion path, which settles the log row
    /// and their own connection row in one <c>SaveChanges</c>. <see cref="StepAsync"/> there could
    /// flush between that row being loaded and being saved, which is a second write against the row
    /// the caller is already holding.
    /// </summary>
    public void Note(string step, string message, int? processed = null)
    {
        var line = IntegrationSyncProgressLog.Line(DateTime.UtcNow, step, message, processed);

        lock (_bufferLock) _pending.Add(line);

        _logger.Debug("Integration sync {Kind}/{Connection} [{Log}]: {Line}", Kind, ConnectionName,
            LogId, line);
    }

    /// <summary>
    /// Persists whatever is buffered. Never throws — see <see cref="StepAsync"/>.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<string> batch;

        lock (_bufferLock)
        {
            if (_pending.Count == 0) return;

            batch = new List<string>(_pending);
            _pending.Clear();
            _lastFlush = DateTime.UtcNow;
        }

        // Serialized: two concurrent flushes would each read the trail, append their own batch and
        // write it back, and the second write would drop the first batch entirely.
        await _writeGate.WaitAsync(ct);

        try
        {
            await using var db = _dalService.GetContext();

            var row = await db.IntegrationSyncLogs.FirstOrDefaultAsync(l => l.Id == LogId, ct);

            if (row == null)
            {
                _logger.Warning("Progress for integration sync {Kind}/{Connection} was dropped: its "
                                + "sync-log row {Log} no longer exists", Kind, ConnectionName, LogId);
                return;
            }

            row.ProgressLog = IntegrationSyncProgressLog.Append(row.ProgressLog, batch);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Re-buffering the batch would make a persistent write failure grow the buffer without
            // bound for the rest of the run, so the batch is dropped and said to be dropped.
            _logger.Warning(ex, "Could not persist {Count} progress line(s) for integration sync "
                                + "{Kind}/{Connection} [{Log}]", batch.Count, Kind, ConnectionName, LogId);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Takes the buffered lines and folds them into <paramref name="existing"/>, for a caller that is
    /// already holding the row and about to write it.
    ///
    /// This is how the posture integrations keep their completion atomic: they settle the log row and
    /// their own connection row in one <c>SaveChanges</c>, and a separate flush against the same row
    /// would either split that write in two or race it.
    /// </summary>
    public string? DrainInto(string? existing)
    {
        List<string> batch;

        lock (_bufferLock)
        {
            if (_pending.Count == 0) return existing;

            batch = new List<string>(_pending);
            _pending.Clear();
            _lastFlush = DateTime.UtcNow;
        }

        return IntegrationSyncProgressLog.Append(existing, batch);
    }

    /// <summary>
    /// Announces the outcome. Call after the row has been settled, so that a GUI woken by the
    /// notification reads the finished row rather than the Running one.
    ///
    /// Idempotent: a second call is ignored, because the two posture services reach their completion
    /// path from both the success branch and a catch, and a run announced twice reads as two runs.
    /// </summary>
    public async Task NotifyFinishedAsync(IntegrationSyncStatus status, string? summary, string? error,
        CancellationToken ct = default)
    {
        lock (_bufferLock)
        {
            if (_completed) return;
            _completed = true;
        }

        await _notifier.FinishedAsync(Kind, ConnectionName, status, summary, error, ct);
    }

    /// <summary>
    /// Settles the run's row, flushes the trail and announces the outcome, in that order.
    ///
    /// For the integrations that have no connection row of their own to update — the issue-tracker and
    /// Jira paths, which used to insert a single already-finished row. The posture services do not use
    /// this: they settle their log row and their connection row together and call
    /// <see cref="DrainInto"/> and <see cref="NotifyFinishedAsync"/> themselves.
    /// </summary>
    /// <param name="apply">Sets the counts on the row. Called with the row about to be saved.</param>
    public async Task CompleteAsync(IntegrationSyncStatus status, string? summary, string? error,
        Action<IntegrationSyncLog>? apply = null, CancellationToken ct = default)
    {
        await StepAsync("finished", $"Run ended {status}." + (error == null ? "" : " " + error), ct: ct);

        await _writeGate.WaitAsync(ct);

        try
        {
            await using var db = _dalService.GetContext();

            var row = await db.IntegrationSyncLogs.FirstOrDefaultAsync(l => l.Id == LogId, ct);

            if (row != null)
            {
                row.FinishedAt = DateTime.UtcNow;
                row.Status = status;
                row.Summary = Truncate(summary, 2000);
                row.ErrorMessage = Truncate(error, 2000);
                row.ProgressLog = DrainInto(row.ProgressLog);

                apply?.Invoke(row);

                await db.SaveChangesAsync(ct);
            }
            else
            {
                _logger.Warning("The integration sync {Kind}/{Connection} finished {Status} but its "
                                + "sync-log row {Log} no longer exists", Kind, ConnectionName, status,
                    LogId);
            }
        }
        catch (Exception ex)
        {
            // Logged and swallowed for the same reason the posture services swallow theirs: this runs
            // on the completion path, including the failure branch of it, and a throw here replaces the
            // real failure with a 500 — which the desktop client's REST wrapper retries immediately,
            // starting another run that fails the same way. IntegrationSyncLedger's reaper settles the
            // row that is left behind.
            _logger.Error(ex, "The integration sync {Kind}/{Connection} finished {Status} but its "
                              + "sync-log row {Log} could not be updated", Kind, ConnectionName, status,
                LogId);
        }
        finally
        {
            _writeGate.Release();
        }

        await NotifyFinishedAsync(status, summary, error, ct);
    }

    /// <summary>
    /// Settles a run that escaped without recording an outcome, and flushes one that did.
    ///
    /// This is why every caller opens a run with <c>await using</c>. Before the run tracker, an
    /// unexpected exception meant no sync-log row at all; now the row exists from the start, so the
    /// same exception would leave it Running until <see cref="IntegrationSyncLedger"/>'s two-hour
    /// reaper settled it — a phantom run in progress on the sync-log screen, and for the integrations
    /// that claim single-flight, a connection that refuses every sync until the horizon passes. Fixing
    /// that in disposal rather than in each caller's catch is what makes it hold for the escape paths
    /// nobody thought of.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        bool settled;

        lock (_bufferLock) settled = _completed;

        if (settled)
        {
            // Completed normally; only the tail of the trail may still be buffered.
            await FlushAsync();
        }
        else
        {
            await CompleteAsync(IntegrationSyncStatus.Failed, null,
                "The run ended without recording an outcome, which means it was interrupted by an "
                + "error that its own error handling did not see.");
        }

        _writeGate.Dispose();
    }

    private static string? Truncate(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..(max - 1)] + "…";
}
