using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DAL.Enums;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace ServerServices.Integrations;

/// <summary>
/// The scheduled sweep that settles sync runs whose process stopped without recording an outcome.
///
/// <see cref="IntegrationSyncLedger.ReapAbandonedAsync"/> already knew how to settle such a row, but
/// only ran when somebody was about to sync that same provider — the one thing a dead process cannot
/// make happen. This gives the reaper a clock of its own.
/// </summary>
public interface IIntegrationSyncReaper
{
    /// <summary>
    /// Settles every Running row older than <see cref="IntegrationSyncLedger.StaleAfter"/>, for every
    /// integration, announces each one, and returns what it settled.
    ///
    /// Never throws: a sweep that fails loudly once an hour is a sweep somebody turns off.
    /// </summary>
    Task<IReadOnlyList<AbandonedRun>> ReapAsync(DateTime nowUtc, CancellationToken ct = default);
}

/// <inheritdoc />
public class IntegrationSyncReaper(
    ILogger logger,
    IDalService dalService,
    IIntegrationSyncNotifier notifier)
    : ServiceBase(logger, dalService), IIntegrationSyncReaper
{
    public async Task<IReadOnlyList<AbandonedRun>> ReapAsync(DateTime nowUtc,
        CancellationToken ct = default)
    {
        IReadOnlyList<AbandonedRun> reaped;

        try
        {
            await using var db = DalService.GetContext();

            reaped = await IntegrationSyncLedger.ReapAllAsync(db, nowUtc, ct);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "The abandoned integration-run sweep could not settle its rows");
            return [];
        }

        if (reaped.Count == 0) return reaped;

        Logger.Warning(
            "The abandoned integration-run sweep settled {Count} run(s) left Running by a stopped "
            + "process: {Runs}", reaped.Count, string.Join(", ", Describe(reaped)));

        foreach (var run in reaped)
        {
            // Announced individually rather than as one summary: "which of my connections is not
            // syncing" is the operator's question, and a count does not answer it. Settling is
            // idempotent, so an hourly sweep cannot announce the same run twice.
            try
            {
                await notifier.FinishedAsync(run.Kind, run.ConnectionName ?? $"#{run.ConnectionId}",
                    IntegrationSyncStatus.Failed, null,
                    IntegrationSyncLedger.AbandonedMessage(run.Age), ct);
            }
            catch (Exception ex)
            {
                // The row is already settled, which is the part that matters. A dead notification
                // channel must not make the sweep look like it failed — the next sweep would find
                // nothing to do, and this run would go unmentioned entirely.
                Logger.Warning(ex,
                    "The abandoned integration-run sweep settled run {Log} but could not announce it",
                    run.LogId);
            }
        }

        return reaped;
    }

    private static IEnumerable<string> Describe(IReadOnlyList<AbandonedRun> reaped)
    {
        foreach (var run in reaped)
            yield return $"{run.Kind}/{run.ConnectionName ?? $"#{run.ConnectionId}"} "
                         + $"[{run.LogId}] stuck {IntegrationSyncLedger.FormatAge(run.Age)}";
    }
}
