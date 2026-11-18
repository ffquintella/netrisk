using Contracts.Importers;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.Tenable;

public partial class TenableService
{
    /// <summary>A Queued request the job host has not picked up in this long is settled, so it cannot refuse every retry.</summary>
    public static readonly TimeSpan QueuedExpiry = TimeSpan.FromHours(24);

    public async Task<TenableSyncResult> RunPendingAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var total = new TenableSyncResult();
        var ranNow = new HashSet<int>();

        await SettleStaleRowsAsync(nowUtc, ct);

        // 1. Manual requests (S40 §8.7), oldest first, each claimed atomically.
        List<IntegrationSyncLog> queued;
        await using (var db = DalService.GetContext())
        {
            queued = await db.IntegrationSyncLogs.AsNoTracking()
                .Where(l => l.Integration == IntegrationKind.Tenable && l.Status == IntegrationSyncStatus.Queued)
                .OrderBy(l => l.Id)
                .ToListAsync(ct);
        }

        foreach (var request in queued)
        {
            if (request.ConnectionId is not { } connectionId || ranNow.Contains(connectionId)) continue;

            var claimed = await ClaimQueuedAsync(request.Id, nowUtc, ct);
            if (claimed is null) continue;

            ranNow.Add(connectionId);
            Add(total, await RunClaimedAsync(claimed, ct));
        }

        // 2. Scheduled runs: the anchored cadence of S40 §6.8, advanced before the run so a long one never stacks.
        foreach (var connectionId in await AdvanceDueAsync(nowUtc, ct))
        {
            if (ranNow.Contains(connectionId)) continue;

            try
            {
                Add(total, await RunScheduledAsync(connectionId, ct));
            }
            catch (IntegrationSyncBusyException ex)
            {
                Logger.Information("Tenable connection {Connection} was skipped: {Message}", connectionId, ex.Message);
                total.Messages.Add(ex.Message);
            }
        }

        return total;
    }

    /// <summary>
    /// The anchored cadence (S40 §6.8): a connection without an anchor is due now and anchored here; a due one advances by
    /// the smallest whole number of intervals that puts it after now. Answers the connections due at this tick.
    /// </summary>
    private async Task<List<int>> AdvanceDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var db = DalService.GetContext();
        var due = new List<int>();

        foreach (var connection in await db.TenableConnections.Where(c => c.Enabled).OrderBy(c => c.Id).ToListAsync(ct))
        {
            var anchor = connection.NextDueAt ?? nowUtc;
            if (anchor > nowUtc) continue;

            connection.NextDueAt = NextDue(anchor, connection.SyncIntervalHours, nowUtc);
            due.Add(connection.Id);
        }

        if (due.Count > 0) await db.SaveChangesAsync(ct);
        return due;
    }

    /// <summary><c>anchor + k·interval</c> with the smallest <c>k ≥ 1</c> that is after <paramref name="nowUtc"/>.</summary>
    internal static DateTime NextDue(DateTime anchor, int intervalHours, DateTime nowUtc)
    {
        var interval = TimeSpan.FromHours(Math.Clamp(intervalHours, MinIntervalHours, MaxIntervalHours));
        var k = Math.Max(1L, (long)Math.Floor((nowUtc - anchor).Ticks / (double)interval.Ticks) + 1);
        return anchor + TimeSpan.FromTicks(interval.Ticks * k);
    }

    /// <summary>Running rows past the heartbeat horizon are settled; Queued rows nobody picked up in a day are too.</summary>
    private async Task SettleStaleRowsAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var db = DalService.GetContext();
        await IntegrationSyncLedger.ReapAbandonedAsync(db, IntegrationKind.Tenable, nowUtc, ct: ct);

        var horizon = nowUtc - QueuedExpiry;
        var expired = await db.IntegrationSyncLogs
            .Where(l => l.Integration == IntegrationKind.Tenable && l.Status == IntegrationSyncStatus.Queued &&
                        l.StartedAt < horizon)
            .ToListAsync(ct);
        foreach (var row in expired)
        {
            row.Status = IntegrationSyncStatus.Failed;
            row.FinishedAt = nowUtc;
            row.ErrorMessage = "The manual request was not picked up by the job host within a day; request it again.";
        }

        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// <c>UPDATE … SET status = Running, started_at = now, heartbeat_at = now WHERE id = ? AND status = Queued</c>, checking
    /// the rows affected (S40 §9.4), so two job hosts never run one request; the in-memory provider takes a tracked update.
    /// </summary>
    internal async Task<IntegrationSyncLog?> ClaimQueuedAsync(int logId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        if (db.Database.IsRelational())
        {
            var won = await db.IntegrationSyncLogs
                .Where(l => l.Id == logId && l.Status == IntegrationSyncStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.Status, IntegrationSyncStatus.Running)
                    .SetProperty(l => l.StartedAt, nowUtc)
                    .SetProperty(l => l.HeartbeatAt, nowUtc)
                    .SetProperty(l => l.Summary, (string?)null), ct) == 1;
            return won ? await db.IntegrationSyncLogs.AsNoTracking().FirstAsync(l => l.Id == logId, ct) : null;
        }

        var row = await db.IntegrationSyncLogs.FirstOrDefaultAsync(
            l => l.Id == logId && l.Status == IntegrationSyncStatus.Queued, ct);
        if (row is null) return null;
        row.Status = IntegrationSyncStatus.Running;
        row.StartedAt = nowUtc;
        row.HeartbeatAt = nowUtc;
        row.Summary = null;
        await db.SaveChangesAsync(ct);
        return row;
    }

    private async Task<TenableSyncResult> RunClaimedAsync(IntegrationSyncLog claimed, CancellationToken ct)
    {
        TenableConnection? connection;
        await using (var db = DalService.GetContext())
        {
            connection = await db.TenableConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == claimed.ConnectionId, ct);
        }

        await using var run = await tracker.ResumeAsync(claimed, TenableClient.ProviderLabel, ct);

        if (connection is null || !connection.Enabled)
        {
            var result = new TenableSyncResult { Errors = 1 };
            await CompleteAsync(run, connection, result,
                connection is null ? "The connection was deleted before the job host ran it." : "The connection is disabled.", ct);
            return result;
        }

        return await RunAsync(connection, run, ct);
    }

    private async Task<TenableSyncResult> RunScheduledAsync(int connectionId, CancellationToken ct)
    {
        TenableConnection connection;
        await using (var db = DalService.GetContext())
        {
            connection = await LoadAsync(db, connectionId, ct);
        }

        await using var run = await tracker.BeginAsync(IntegrationKind.Tenable, connection.Id, connection.Name,
            TenableClient.ProviderLabel, singleFlight: true, ct);
        return await RunAsync(connection, run, ct);
    }

    /// <summary>
    /// One run: assets (T267, with the tag rules of T268) before findings (T266), so a finding lands on the host the
    /// identity chain chose. Each step's cursor advances only after the step succeeds, saved on its own, so a later failure
    /// never loses an earlier step's progress and a failed step is re-read from the same point next time.
    /// </summary>
    private async Task<TenableSyncResult> RunAsync(TenableConnection connection, IntegrationSyncRun run, CancellationToken ct)
    {
        var result = new TenableSyncResult { Runs = 1 };
        TenableCredentials? credentials = null;

        try
        {
            credentials = await ResolveCredentialsAsync(connection, ct);

            if (connection.SyncAssets) await SyncAssetsAsync(connection, credentials, run, result, ct);
            else await run.StepAsync("assets", "Skipped: the connection does not sync assets.", ct: ct);

            if (connection.SyncFindings) await SyncFindingsAsync(connection, credentials, run, result, ct);
            else await run.StepAsync("findings", "Skipped: the connection does not sync findings.", ct: ct);

            await CompleteAsync(run, connection, result, null, ct, credentials);
        }
        catch (OperationCanceledException)
        {
            await CompleteAsync(run, connection, result, "The run was cancelled.", CancellationToken.None, credentials);
            throw;
        }
        catch (Exception ex)
        {
            result.Errors++;
            var message = Redact(ex.Message, credentials);
            result.Messages.Add(message);

            // The exception type and the redacted message only: an inner exception or a stack trace could carry what the
            // HTTP layer was handed, and the process log is in scope of the non-disclosure rule (S40 §11.6).
            Logger.Error("Tenable sync of connection {Connection} failed ({Type}): {Message}", connection.Name,
                ex.GetType().Name, message);

            await CompleteAsync(run, connection, result, message, CancellationToken.None, credentials);
        }

        return result;
    }

    // --- T266: findings ----------------------------------------------------------------------------------------------

    private async Task SyncFindingsAsync(TenableConnection connection, TenableCredentials credentials,
        IntegrationSyncRun run, TenableSyncResult result, CancellationToken ct)
    {
        var requestedAt = DateTime.UtcNow;
        var since = connection.FindingsCursor ?? requestedAt.AddDays(-connection.InitialWindowDays);

        await run.StepAsync("findings", $"Requesting the vulnerability export since {since:yyyy-MM-dd HH:mm} UTC.", ct: ct);

        var records = new List<TenableVulnerability>();
        await client.ExportVulnerabilitiesAsync(connection, credentials, since,
            chunk =>
            {
                records.AddRange(TenableNormalizer.ParseVulnerabilities(chunk));
                return Task.CompletedTask;
            },
            message => run.StepAsync("findings", Redact(message, credentials), ct: ct), ct);

        result.FindingsReceived += records.Count;
        await run.StepAsync("findings", "Vulnerability records received.", records.Count, ct);

        var open = records.Where(r => !r.IsFixed).ToList();
        var fixedOnes = records.Where(r => r.IsFixed).ToList();

        if (open.Count > 0)
        {
            await BindToKnownHostsAsync(open, ct);

            var parsed = new ImportResult
            {
                DetectedTool = TenableNormalizer.ImporterName,
                // Incremental: an export since the cursor is a slice, and treating it as full would close every finding
                // outside it. A finding Tenable fixed arrives as FIXED and is closed explicitly below.
                IsFullScan = false,
                ScanDate = requestedAt
            };
            parsed.Findings.AddRange(open.Select(r => r.Finding));

            var import = await ingestion.IngestAsync(parsed, new ImportIngestionRequest
            {
                Importer = TenableNormalizer.ImporterName,
                FileName = $"Tenable {connection.Name} {requestedAt:yyyy-MM-dd HH:mm}",
                EntityId = connection.EntityId
            }, Throttled(run, ct), ct);

            result.FindingsCreated += import.NewCount;
            result.FindingsUpdated += import.UpdatedCount;
        }

        if (fixedOnes.Count > 0) await CloseFixedAsync(fixedOnes, run, result, ct);

        await RecordEpssAsync(records, run, ct);

        await using (var db = DalService.GetContext())
        {
            var stored = await db.TenableConnections.FirstAsync(c => c.Id == connection.Id, ct);
            stored.FindingsCursor = requestedAt;
            await db.SaveChangesAsync(ct);
        }

        await run.StepAsync("findings",
            $"{result.FindingsCreated} finding(s) created, {result.FindingsUpdated} updated, {result.FindingsFixed} closed as fixed.",
            ct: ct);
    }

    /// <summary>
    /// Points each record's host at the host the asset sync matched through <c>host_external_ids</c>, by giving the
    /// pipeline that host's own IP and names: the pipeline looks hosts up by IP, then name, and would otherwise create a
    /// second host for an asset whose export record carries a different address than the inventory.
    /// </summary>
    private async Task BindToKnownHostsAsync(List<TenableVulnerability> records, CancellationToken ct)
    {
        var uuids = records.Select(r => r.Asset.Uuid).Where(u => u is not null).Select(u => u!).Distinct().ToList();
        if (uuids.Count == 0) return;

        await using var db = DalService.GetContext();
        var hosts = new Dictionary<string, (string? Ip, string? Name, string? Fqdn)>(StringComparer.OrdinalIgnoreCase);
        foreach (var slice in uuids.Chunk(500))
        {
            var rows = await (from x in db.HostExternalIds.AsNoTracking()
                    where x.Provider == ProviderName && slice.Contains(x.ExternalId)
                    join h in db.Hosts.AsNoTracking() on x.HostId equals h.Id
                    select new { x.ExternalId, h.Ip, h.HostName, h.Fqdn })
                .ToListAsync(ct);
            foreach (var row in rows) hosts[row.ExternalId] = (row.Ip, row.HostName, row.Fqdn);
        }

        foreach (var record in records)
        {
            if (record.Asset.Uuid is not { } uuid || !hosts.TryGetValue(uuid, out var host) || record.Finding.Host is null)
                continue;
            if (host.Ip is null && host.Name is null && host.Fqdn is null) continue;
            record.Finding.Host.Ip = host.Ip;
            record.Finding.Host.HostName = host.Name ?? record.Finding.Host.HostName;
            record.Finding.Host.Fqdn = host.Fqdn ?? record.Finding.Host.Fqdn;
        }
    }

    /// <summary>
    /// A FIXED record closes its finding through the lifecycle (S40 §9.4), never through the ingestion pipeline, which
    /// would read a fresh sighting as a regression and reopen it. Suppressed and already-closed findings are left alone;
    /// a fixed record NetRisk never imported has nothing to close.
    /// </summary>
    private async Task CloseFixedAsync(List<TenableVulnerability> fixedOnes, IntegrationSyncRun run, TenableSyncResult result,
        CancellationToken ct)
    {
        var ids = fixedOnes.Select(f => f.Finding.ToolUniqueId).Where(i => i is not null).Select(i => i!).Distinct().ToList();
        var targets = new List<(int Id, FindingStatus Status)>();

        await using (var db = DalService.GetContext())
        {
            foreach (var slice in ids.Chunk(500))
                targets.AddRange((await db.Vulnerabilities.AsNoTracking()
                        .Where(v => v.ImportSource == TenableNormalizer.ImporterName && v.ToolUniqueId != null &&
                                    slice.Contains(v.ToolUniqueId))
                        .Select(v => new { v.Id, v.LifecycleStatus })
                        .ToListAsync(ct))
                    .Select(v => (v.Id, v.LifecycleStatus)));
        }

        foreach (var (id, status) in targets)
        {
            if (status == FindingStatus.Mitigated || status.IsSuppressed() || !status.IsOpen()) continue;
            try
            {
                await lifecycle.TransitionAsync(id, FindingStatus.Mitigated, null, FindingStatusChangeSource.Job,
                    "Reported as fixed by Tenable Vulnerability Management.");
                result.FindingsFixed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Errors++;
                result.Messages.Add($"Finding {id}: {ex.Message}");
            }
        }

        await run.StepAsync("findings", $"{result.FindingsFixed} finding(s) closed as fixed by Tenable.", fixedOnes.Count, ct);
    }

    /// <summary>The export's EPSS per CVE as Tenable readings (S45, T162 precedence); a failure here does not fail the run.</summary>
    private async Task RecordEpssAsync(List<TenableVulnerability> records, IntegrationSyncRun run, CancellationToken ct)
    {
        var byCve = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Where(r => r.Epss is not null))
        foreach (var cve in record.Finding.Cves)
            byCve[cve] = byCve.TryGetValue(cve, out var seen) ? Math.Max(seen, record.Epss!.Value) : record.Epss!.Value;

        if (byCve.Count == 0) return;

        try
        {
            var changed = await exploitationSignals.RecordTenableEpssAsync(byCve, DateTime.UtcNow, ct);
            await run.StepAsync("epss", $"EPSS recorded for {byCve.Count} CVE(s); {changed} finding(s) re-projected.", ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error(ex, "Recording the Tenable EPSS readings failed; the findings were ingested");
        }
    }

    private static Func<ImportProgress, Task> Throttled(IntegrationSyncRun run, CancellationToken ct)
    {
        var lastPercent = -1;
        return progress =>
        {
            var percent = progress.Percent ?? -1;
            if (percent == lastPercent) return Task.CompletedTask;
            lastPercent = percent;
            return run.StepAsync("findings",
                $"Persisting findings: {percent}% — {progress.Created} created, {progress.Updated} updated, {progress.Skipped} skipped.",
                progress.Processed, ct);
        };
    }

    // --- completion --------------------------------------------------------------------------------------------------

    /// <summary>Settles the log row and the connection row in one save, then announces the outcome (the posture pattern).</summary>
    private async Task CompleteAsync(IntegrationSyncRun run, TenableConnection? connection, TenableSyncResult result,
        string? error, CancellationToken ct, TenableCredentials? credentials = null)
    {
        error = error is null ? null : Redact(error, credentials);
        var status = error != null ? IntegrationSyncStatus.Failed
            : result.Errors > 0 ? IntegrationSyncStatus.PartiallySucceeded
            : IntegrationSyncStatus.Succeeded;

        var summary = Truncate(Redact(
            $"{result.AssetsReceived} asset(s): {result.HostsCreated} host(s) created, {result.HostsUpdated} updated"
            + (result.HostsAssignedByRule + result.HostsReassignedByRule == 0
                ? ""
                : $", {result.HostsAssignedByRule} attributed and {result.HostsReassignedByRule} reattributed by rule")
            + $", {result.ScoresWritten} reading(s) opened; {result.FindingsCreated} finding(s) created, "
            + $"{result.FindingsUpdated} updated, {result.FindingsFixed} closed as fixed.", credentials), 2000);

        await using var db = DalService.GetContext();
        var stored = await db.IntegrationSyncLogs.FirstOrDefaultAsync(l => l.Id == run.LogId, ct);
        if (stored != null)
        {
            stored.FinishedAt = DateTime.UtcNow;
            stored.HeartbeatAt = stored.FinishedAt;
            stored.Status = status;
            stored.CreatedCount = result.HostsCreated + result.FindingsCreated;
            stored.UpdatedCount = result.HostsUpdated + result.FindingsUpdated;
            stored.FailedCount = result.Errors;
            stored.Summary = summary;
            stored.ErrorMessage = Truncate(error, 2000);
            run.Note("finished", $"Run ended {status}." + (error == null ? "" : " " + error));
            stored.ProgressLog = Redact(run.DrainInto(stored.ProgressLog), credentials);
        }

        if (connection != null &&
            await db.TenableConnections.FirstOrDefaultAsync(c => c.Id == connection.Id, ct) is { } storedConnection)
        {
            storedConnection.LastSyncAt = DateTime.UtcNow;
            storedConnection.LastSyncStatus = status;
            storedConnection.LastSyncError = Truncate(error, 2000);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Swallowed like the posture services' settle: this runs on the failure path too, and the sweep settles the row.
            Logger.Error("The Tenable sync log {Log} could not be settled ({Type})", run.LogId, ex.GetType().Name);
        }

        await run.NotifyFinishedAsync(status, summary, error, ct);
    }

    private static void Add(TenableSyncResult total, TenableSyncResult one)
    {
        total.Runs += one.Runs;
        total.AssetsReceived += one.AssetsReceived;
        total.HostsCreated += one.HostsCreated;
        total.HostsUpdated += one.HostsUpdated;
        total.HostsAssignedByRule += one.HostsAssignedByRule;
        total.HostsReassignedByRule += one.HostsReassignedByRule;
        total.ScoresWritten += one.ScoresWritten;
        total.FindingsReceived += one.FindingsReceived;
        total.FindingsCreated += one.FindingsCreated;
        total.FindingsUpdated += one.FindingsUpdated;
        total.FindingsFixed += one.FindingsFixed;
        total.Errors += one.Errors;
        total.Messages.AddRange(one.Messages);
    }
}
