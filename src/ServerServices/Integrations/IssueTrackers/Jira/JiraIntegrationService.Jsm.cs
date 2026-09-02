using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// The Service Management mirror (Track 4 milestone 4.6): pull the configured queues, upsert the
/// requests, record their SLA cycles, and raise a notification for a breach.
/// </summary>
public partial class JiraIntegrationService
{
    public async Task<JsmSyncResult> SyncServiceManagementAsync(int connectionId, int? userId = null)
    {
        var (connection, token, settings) = await ResolveAsync(connectionId);

        var result = new JsmSyncResult();

        if (!settings.JsmEnabled)
        {
            result.Messages.Add("Service Management is not enabled on this connection.");
            return result;
        }

        await using var db = DalService.GetContext();

        var queues = await db.JiraQueueImports
            .Where(q => q.ConnectionId == connectionId && q.Enabled)
            .ToListAsync();

        if (queues.Count == 0)
        {
            result.Messages.Add(
                "No queues are selected for import, so there is nothing to mirror. Requests already "
                + "linked to a NetRisk record are still refreshed.");
        }

        var typeFilter = ParseTypeFilter(settings.RequestTypeFilter);

        // Deduplicated across queues before any request is fetched. Two queues in a service desk
        // routinely overlap, and mirroring an issue twice in one pass costs two API calls per SLA read
        // to write the same row.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var queue in queues)
        {
            result.QueuesExamined++;

            try
            {
                foreach (var key in await jsm.GetQueueIssueKeysAsync(connection, token,
                             queue.ServiceDeskId, queue.QueueId, queue.MaxRequests))
                    keys.Add(key);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.Messages.Add($"Queue {queue.QueueName ?? queue.QueueId.ToString()}: {ex.Message}");
            }
        }

        // Requests that are already linked to a NetRisk record are refreshed whatever the queue
        // configuration says. A link is a stronger statement of interest than a queue selection, and
        // letting a linked ticket's status go stale because somebody reorganised the queues is the
        // failure this catches.
        foreach (var linked in await db.FindingIssueLinks
                     .Where(l => l.ConnectionId == connectionId)
                     .Select(l => l.IssueKey)
                     .ToListAsync())
            keys.Add(linked);

        foreach (var key in keys)
        {
            try
            {
                var request = await jsm.GetRequestAsync(connection, token, key);

                if (request == null) continue;

                if (typeFilter.Count > 0
                    && request.RequestTypeId != null
                    && !typeFilter.Contains(request.RequestTypeId))
                    continue;

                result.RequestsExamined++;

                var stored = await UpsertRequestAsync(db, connectionId, request, result);

                if (settings.ImportSlas)
                {
                    // Only fetched separately when the expand did not already carry them: expand=sla
                    // is one call and this is a second per request, which on a five-hundred-request
                    // queue is five hundred avoidable round trips.
                    var cycles = request.Slas.Count > 0
                        ? request.Slas
                        : await jsm.GetSlaAsync(connection, token, key);

                    await UpsertSlasAsync(db, stored, cycles, settings, result);
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.Messages.Add($"{key}: {ex.Message}");
                Logger.Warning(ex, "Mirroring {Issue} from connection {Connection} failed", key,
                    connectionId);
            }
        }

        var settingsRow = await db.JiraConnectionSettings.FirstAsync(s => s.ConnectionId == connectionId);
        settingsRow.LastJsmSyncAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await RecordJsmSyncAsync(connectionId, connection.Name, result);

        Logger.Information(
            "JSM sync of connection {Connection} by user {User}: {Examined} request(s), "
            + "{Created} created, {Updated} updated, {Breaches} breach(es), {Errors} error(s)",
            connectionId, userId, result.RequestsExamined, result.RequestsCreated,
            result.RequestsUpdated, result.Breaches, result.Errors);

        return result;
    }

    public async Task<JsmSyncResult> SyncDueServiceManagementAsync(DateTime nowUtc)
    {
        await using var db = DalService.GetContext();

        // Reuses the connection's existing poll interval rather than adding a second schedule. One
        // knob per connection is what an operator can reason about; two would raise the question of
        // which of them governs a queue that feeds a linked finding.
        var due = await db.JiraConnectionSettings
            .Where(s => s.JsmEnabled)
            .Join(db.IssueTrackerConnections.Where(c => c.Enabled
                                                        && c.Provider == IssueTrackerProviderKind.Jira),
                s => s.ConnectionId, c => c.Id, (s, c) => new { Settings = s, Connection = c })
            .ToListAsync();

        var total = new JsmSyncResult();

        foreach (var candidate in due)
        {
            var interval = Math.Max(candidate.Connection.PollIntervalMinutes, 5);

            if (candidate.Settings.LastJsmSyncAt is { } last
                && last.AddMinutes(interval) > nowUtc) continue;

            try
            {
                var one = await SyncServiceManagementAsync(candidate.Connection.Id);

                total.QueuesExamined += one.QueuesExamined;
                total.RequestsExamined += one.RequestsExamined;
                total.RequestsCreated += one.RequestsCreated;
                total.RequestsUpdated += one.RequestsUpdated;
                total.SlaCyclesRecorded += one.SlaCyclesRecorded;
                total.Breaches += one.Breaches;
                total.Errors += one.Errors;
                total.Messages.AddRange(one.Messages.Take(5));
            }
            catch (Exception ex)
            {
                // Caught per connection: one unreachable Jira must not stop the job from mirroring
                // every other customer's service desk.
                total.Errors++;
                total.Messages.Add($"{candidate.Connection.Name}: {ex.Message}");
                Logger.Warning(ex, "JSM sync of connection {Connection} failed",
                    candidate.Connection.Id);
            }
        }

        return total;
    }

    public async Task<List<JiraServiceRequestView>> GetMirroredRequestsAsync(int connectionId,
        bool breachedOnly = false, int limit = 200)
    {
        await EnsureConnectionExistsAsync(connectionId);

        await using var db = DalService.GetContext();

        var query = db.JiraServiceRequests
            .Include(r => r.Slas)
            .Where(r => r.ConnectionId == connectionId);

        if (breachedOnly) query = query.Where(r => r.Slas.Any(s => s.Breached));

        return (await query
                .OrderByDescending(r => r.UpdatedAtRemote ?? r.FirstSeenAt)
                .Take(Math.Clamp(limit, 1, 2000))
                .ToListAsync())
            .Select(ToView).ToList();
    }

    public async Task<JiraServiceRequestView> GetMirroredRequestAsync(int connectionId, string issueKey)
    {
        await EnsureConnectionExistsAsync(connectionId);

        await using var db = DalService.GetContext();

        var request = await db.JiraServiceRequests
                          .Include(r => r.Slas)
                          .FirstOrDefaultAsync(r => r.ConnectionId == connectionId
                                                    && r.IssueKey == issueKey)
                      ?? throw new DataNotFoundException("jira service request", issueKey,
                          new Exception($"{issueKey} is not mirrored on connection {connectionId}."));

        return ToView(request);
    }

    // --- upsert -----------------------------------------------------------------------------

    private async Task<JiraServiceRequest> UpsertRequestAsync(AuditableContext db, int connectionId,
        JsmRequest request, JsmSyncResult result)
    {
        var stored = await db.JiraServiceRequests
            .FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.IssueKey == request.IssueKey);

        if (stored == null)
        {
            stored = new JiraServiceRequest
            {
                ConnectionId = connectionId,
                IssueKey = request.IssueKey,
                FirstSeenAt = DateTime.UtcNow
            };

            db.JiraServiceRequests.Add(stored);
            result.RequestsCreated++;
        }
        else
        {
            result.RequestsUpdated++;
        }

        stored.IssueId = Clip(request.IssueId, 128);
        stored.ServiceDeskId = request.ServiceDeskId;
        stored.RequestTypeId = Clip(request.RequestTypeId, 128);
        stored.RequestTypeName = Clip(request.RequestTypeName, 255);
        stored.Summary = request.Summary;
        stored.StatusName = Clip(request.StatusName, 128);
        stored.StatusCategory = Clip(request.StatusCategory, 64);
        stored.ReporterAccountId = Clip(request.ReporterAccountId, 128);
        stored.ReporterDisplayName = Clip(request.ReporterDisplayName, 255);
        stored.OrganizationName = Clip(request.OrganizationName, 255);
        stored.PriorityName = Clip(request.PriorityName, 128);
        stored.AssigneeDisplayName = Clip(request.AssigneeDisplayName, 255);
        stored.CreatedAtRemote = request.CreatedAt;
        stored.UpdatedAtRemote = request.UpdatedAt;
        stored.IsClosed = request.IsClosed;
        stored.RequestUrl = Clip(request.RequestUrl, 1024);
        stored.LastSyncedAt = DateTime.UtcNow;
        stored.SyncError = null;

        // Saved per request rather than once at the end: a failure half-way through five hundred
        // requests otherwise discards every mirror row read before it, and the retry starts over
        // against the same rate limit.
        await db.SaveChangesAsync();

        return stored;
    }

    /// <summary>
    /// Upserts one request's SLA cycles, keyed on (request, metric, cycle start).
    ///
    /// The cycle start is part of the key because a reopened request starts a second cycle of the same
    /// metric: keying on the metric alone would overwrite the first cycle's breach with the second
    /// cycle's clean state, and the breach would vanish from the record.
    /// </summary>
    private async Task UpsertSlasAsync(AuditableContext db, JiraServiceRequest request,
        IReadOnlyList<JsmSlaCycle> cycles, JiraConnectionSettings settings, JsmSyncResult result)
    {
        if (cycles.Count == 0) return;

        var existing = await db.JiraRequestSlas
            .Where(s => s.RequestId == request.Id)
            .ToListAsync();

        foreach (var cycle in cycles)
        {
            var stored = existing.FirstOrDefault(s =>
                string.Equals(s.MetricName, cycle.MetricName, StringComparison.OrdinalIgnoreCase)
                && s.CycleStartAt == cycle.CycleStartAt);

            var isNewBreach = cycle.Breached && (stored == null || !stored.Breached);

            if (stored == null)
            {
                stored = new JiraRequestSla
                {
                    RequestId = request.Id,
                    MetricName = Clip(cycle.MetricName, 255) ?? "SLA",
                    CycleStartAt = cycle.CycleStartAt
                };

                db.JiraRequestSlas.Add(stored);
            }

            stored.MetricId = Clip(cycle.MetricId, 128);
            stored.IsOngoing = cycle.IsOngoing;
            stored.Breached = cycle.Breached;
            stored.Paused = cycle.Paused;
            stored.GoalDurationMs = cycle.GoalDurationMs;
            stored.ElapsedMs = cycle.ElapsedMs;
            stored.RemainingMs = cycle.RemainingMs;
            stored.CycleStopAt = cycle.CycleStopAt;
            stored.CapturedAt = DateTime.UtcNow;

            result.SlaCyclesRecorded++;

            if (!isNewBreach) continue;

            result.Breaches++;

            // Only a *new* breach notifies. Re-notifying every sync for a cycle that breached last
            // week is how a channel gets muted, and a muted channel is worse than no channel.
            if (settings.SlaBreachNotifications)
                await NotifyBreachAsync(request, stored);
        }

        await db.SaveChangesAsync();
    }

    private async Task NotifyBreachAsync(JiraServiceRequest request, JiraRequestSla sla)
    {
        try
        {
            await notifications.JsmSlaBreachedAsync(request.IssueKey, request.Summary, sla.MetricName,
                request.RequestUrl, request.ReporterDisplayName, sla.RemainingMs);
        }
        catch (Exception ex)
        {
            // Swallowed, like every other notification in Track 4: a breach that could not be
            // announced must not fail the sync that discovered it.
            Logger.Warning(ex, "Could not raise the JSM SLA breach notification for {Issue}",
                request.IssueKey);
        }
    }

    private static HashSet<string> ParseTypeFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

    private async Task RecordJsmSyncAsync(int connectionId, string connectionName, JsmSyncResult result)
    {
        await using var db = DalService.GetContext();

        db.IntegrationSyncLogs.Add(new IntegrationSyncLog
        {
            Integration = IntegrationKind.JiraServiceManagement,
            ConnectionId = connectionId,
            ConnectionName = connectionName,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = result.Errors == 0
                ? IntegrationSyncStatus.Succeeded
                : result.RequestsExamined > 0
                    ? IntegrationSyncStatus.PartiallySucceeded
                    : IntegrationSyncStatus.Failed,
            CreatedCount = result.RequestsCreated,
            UpdatedCount = result.RequestsUpdated,
            FailedCount = result.Errors,
            Summary = Truncate(
                $"{result.QueuesExamined} queue(s), {result.RequestsExamined} request(s), "
                + $"{result.SlaCyclesRecorded} SLA cycle(s), {result.Breaches} new breach(es)."
                + (result.Messages.Count == 0
                    ? ""
                    : " " + string.Join(" | ", result.Messages.Take(20))), 2000)
        });

        await db.SaveChangesAsync();
    }

    private static JiraServiceRequestView ToView(JiraServiceRequest request) => new()
    {
        Id = request.Id,
        ConnectionId = request.ConnectionId,
        IssueKey = request.IssueKey,
        RequestTypeName = request.RequestTypeName,
        Summary = request.Summary,
        StatusName = request.StatusName,
        StatusCategory = request.StatusCategory,
        ReporterDisplayName = request.ReporterDisplayName,
        OrganizationName = request.OrganizationName,
        PriorityName = request.PriorityName,
        AssigneeDisplayName = request.AssigneeDisplayName,
        IsClosed = request.IsClosed,
        RequestUrl = request.RequestUrl,
        CreatedAtRemote = request.CreatedAtRemote,
        UpdatedAtRemote = request.UpdatedAtRemote,
        LastSyncedAt = request.LastSyncedAt,
        SyncError = request.SyncError,
        AnySlaBreached = (request.Slas ?? new List<JiraRequestSla>()).Any(s => s.Breached),
        Slas = (request.Slas ?? new List<JiraRequestSla>())
            .OrderBy(s => s.MetricName).ThenBy(s => s.CycleStartAt)
            .Select(s => new JiraRequestSlaView
            {
                Id = s.Id,
                MetricName = s.MetricName,
                IsOngoing = s.IsOngoing,
                Breached = s.Breached,
                Paused = s.Paused,
                GoalDurationMs = s.GoalDurationMs,
                ElapsedMs = s.ElapsedMs,
                RemainingMs = s.RemainingMs,
                CycleStartAt = s.CycleStartAt,
                CycleStopAt = s.CycleStopAt
            }).ToList()
    };
}
