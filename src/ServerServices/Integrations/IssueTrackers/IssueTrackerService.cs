using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Integrations.IssueTrackers;

/// <summary>
/// Issue-tracker connections, links and bi-directional sync (Track 4 milestone 4.2).
/// </summary>
public class IssueTrackerService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    IIssueTrackerProviderRegistry registry,
    IFindingLifecycleService lifecycle,
    INotificationEventPublisher notifications,
    Microsoft.Extensions.Configuration.IConfiguration configuration)
    : ServiceBase(logger, dalService), IIssueTrackerService
{
    /// <summary>
    /// Default severity→priority maps, per provider, used when a connection has none. Ships with a
    /// sensible mapping rather than nothing, because a connection whose priority is always the project
    /// default is a connection whose criticals look like everything else.
    /// </summary>
    internal static readonly Dictionary<IssueTrackerProviderKind, Dictionary<int, string>> DefaultPriorities =
        new()
        {
            [IssueTrackerProviderKind.Jira] = new()
                { [4] = "Highest", [3] = "High", [2] = "Medium", [1] = "Low" },
            [IssueTrackerProviderKind.GitHub] = new()
                { [4] = "critical", [3] = "high", [2] = "medium", [1] = "low" },
            [IssueTrackerProviderKind.GitLab] = new()
                { [4] = "critical", [3] = "high", [2] = "medium", [1] = "low" },
            // ADO's Priority field is 1 (highest) to 4 (lowest) — the inverse of NetRisk's scale, which
            // is exactly the kind of thing a default has to get right so nobody has to notice it.
            [IssueTrackerProviderKind.AzureDevOps] = new()
                { [4] = "1", [3] = "2", [2] = "3", [1] = "4" }
        };

    private string? BaseUrl => configuration["app:baseUrl"]?.TrimEnd('/');

    // --- connections ------------------------------------------------------------------------

    public async Task<List<IssueTrackerConnectionView>> GetConnectionsAsync(bool includeDisabled = true)
    {
        await using var db = DalService.GetContext();

        var connections = await db.IssueTrackerConnections
            .Include(c => c.StatusMappings)
            .Where(c => includeDisabled || c.Enabled)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return connections.Select(ToView).ToList();
    }

    public async Task<IssueTrackerConnectionView> GetConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();
        return ToView(await LoadAsync(db, id));
    }

    public async Task<IssueTrackerConnectionView> CreateConnectionAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret, int? userId)
    {
        Validate(connection);

        await using var db = DalService.GetContext();

        if (await db.IssueTrackerConnections.AnyAsync(c => c.Name == connection.Name))
            throw new InvalidParameterException(nameof(connection.Name),
                $"An issue-tracker connection named '{connection.Name}' already exists.");

        var stored = new IssueTrackerConnection
        {
            Name = connection.Name.Trim(),
            Provider = connection.Provider,
            BaseUrl = connection.BaseUrl.TrimEnd('/'),
            ProjectKey = connection.ProjectKey.Trim(),
            IssueType = connection.IssueType,
            AuthUser = connection.AuthUser,
            EncryptedToken = protector.Protect(token),
            EncryptedWebhookSecret = protector.Protect(webhookSecret),
            PriorityMappingJson = connection.PriorityMappingJson,
            TitleTemplate = connection.TitleTemplate,
            DescriptionTemplate = connection.DescriptionTemplate,
            DefaultLabels = connection.DefaultLabels,
            EntityId = connection.EntityId,
            Enabled = connection.Enabled,
            AutoCreateMinSeverity = connection.AutoCreateMinSeverity,
            PushFindingUpdates = connection.PushFindingUpdates,
            PollIntervalMinutes = connection.PollIntervalMinutes <= 0 ? 15 : connection.PollIntervalMinutes,
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        db.IssueTrackerConnections.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("Issue-tracker connection {Name} ({Provider}) created by user {User}",
            stored.Name, stored.Provider, userId);

        return ToView(stored);
    }

    public async Task<IssueTrackerConnectionView> UpdateConnectionAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret, int? userId)
    {
        Validate(connection);

        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, connection.Id);

        if (await db.IssueTrackerConnections.AnyAsync(c => c.Name == connection.Name && c.Id != connection.Id))
            throw new InvalidParameterException(nameof(connection.Name),
                $"An issue-tracker connection named '{connection.Name}' already exists.");

        stored.Name = connection.Name.Trim();
        stored.Provider = connection.Provider;
        stored.BaseUrl = connection.BaseUrl.TrimEnd('/');
        stored.ProjectKey = connection.ProjectKey.Trim();
        stored.IssueType = connection.IssueType;
        stored.AuthUser = connection.AuthUser;
        stored.PriorityMappingJson = connection.PriorityMappingJson;
        stored.TitleTemplate = connection.TitleTemplate;
        stored.DescriptionTemplate = connection.DescriptionTemplate;
        stored.DefaultLabels = connection.DefaultLabels;
        stored.EntityId = connection.EntityId;
        stored.Enabled = connection.Enabled;
        stored.AutoCreateMinSeverity = connection.AutoCreateMinSeverity;
        stored.PushFindingUpdates = connection.PushFindingUpdates;
        stored.PollIntervalMinutes = connection.PollIntervalMinutes <= 0 ? 15 : connection.PollIntervalMinutes;
        stored.UpdatedAt = DateTime.UtcNow;

        // Null means "unchanged", which is what a form that never received the secret sends back.
        if (token != null) stored.EncryptedToken = protector.Protect(token);
        if (webhookSecret != null) stored.EncryptedWebhookSecret = protector.Protect(webhookSecret);

        await db.SaveChangesAsync();

        Logger.Information("Issue-tracker connection {Id} updated by user {User}", stored.Id, userId);

        return ToView(stored);
    }

    public async Task DeleteConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);

        db.IssueTrackerConnections.Remove(stored);
        await db.SaveChangesAsync();

        Logger.Information("Issue-tracker connection {Id} ({Name}) deleted", id, stored.Name);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);
        var provider = ProviderFor(stored);

        try
        {
            return await provider.TestConnectionAsync(stored, protector.Unprotect(stored.EncryptedToken));
        }
        catch (SecretProtectionException ex)
        {
            return ConnectionTestResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Warning("Testing issue-tracker connection {Id} threw: {Message}", id, ex.Message);
            return ConnectionTestResult.Fail($"The test failed: {ex.Message}");
        }
    }

    public IReadOnlyList<(IssueTrackerProviderKind Kind, string Name, IssueTrackerCapabilities Capabilities)>
        GetProviders() =>
        registry.All.Select(p => (p.Kind, p.Name, p.Capabilities)).ToList();

    // --- status mappings --------------------------------------------------------------------

    public async Task<List<IssueStatusMappingView>> GetStatusMappingsAsync(int connectionId)
    {
        await using var db = DalService.GetContext();

        return await db.IssueStatusMappings
            .Where(m => m.ConnectionId == connectionId)
            .OrderBy(m => m.ExternalStatus)
            .Select(m => new IssueStatusMappingView
            {
                Id = m.Id,
                ExternalStatus = m.ExternalStatus,
                Action = m.Action,
                OutboundTransition = m.OutboundTransition
            })
            .ToListAsync();
    }

    public async Task<List<IssueStatusMappingView>> SetStatusMappingsAsync(int connectionId,
        IReadOnlyList<IssueStatusMapping> mappings)
    {
        await using var db = DalService.GetContext();

        await LoadAsync(db, connectionId);

        var duplicates = mappings
            .GroupBy(m => m.ExternalStatus?.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidParameterException(nameof(mappings),
                $"Each external status may be mapped once. Repeated: {string.Join(", ", duplicates)}.");

        if (mappings.Any(m => string.IsNullOrWhiteSpace(m.ExternalStatus)))
            throw new InvalidParameterException(nameof(mappings),
                "A status mapping needs the tracker's status name.");

        var existing = await db.IssueStatusMappings.Where(m => m.ConnectionId == connectionId).ToListAsync();
        db.IssueStatusMappings.RemoveRange(existing);

        foreach (var mapping in mappings)
        {
            db.IssueStatusMappings.Add(new IssueStatusMapping
            {
                ConnectionId = connectionId,
                ExternalStatus = mapping.ExternalStatus.Trim(),
                Action = mapping.Action,
                OutboundTransition = string.IsNullOrWhiteSpace(mapping.OutboundTransition)
                    ? null
                    : mapping.OutboundTransition.Trim()
            });
        }

        await db.SaveChangesAsync();

        Logger.Information("Status mappings for connection {Id} replaced with {Count} row(s)",
            connectionId, mappings.Count);

        return await GetStatusMappingsAsync(connectionId);
    }

    // --- links ------------------------------------------------------------------------------

    public async Task<List<FindingIssueLinkView>> GetLinksForFindingAsync(int findingId)
    {
        await using var db = DalService.GetContext();

        var links = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .Where(l => l.VulnerabilityId == findingId)
            .OrderBy(l => l.Id)
            .ToListAsync();

        return links.Select(ToView).ToList();
    }

    public async Task<IssueDraft> PreviewAsync(int connectionId, int findingId)
    {
        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, connectionId);
        var finding = await LoadFindingAsync(db, findingId);

        return BuildDraft(connection, finding, await AssetNameAsync(db, finding));
    }

    public async Task<FindingIssueLinkView> CreateIssueAsync(int connectionId, int findingId, int? userId)
    {
        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, connectionId);

        if (!connection.Enabled)
            throw new InvalidParameterException(nameof(connectionId),
                $"Connection '{connection.Name}' is disabled.");

        var finding = await LoadFindingAsync(db, findingId);

        // Idempotent per (connection, finding). Clicking "Create issue" twice — or a retried request —
        // must not file two tickets for the same finding.
        var existing = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .FirstOrDefaultAsync(l => l.ConnectionId == connectionId && l.VulnerabilityId == findingId);

        if (existing != null) return ToView(existing);

        var provider = ProviderFor(connection);
        var draft = BuildDraft(connection, finding, await AssetNameAsync(db, finding));

        var issue = await provider.CreateIssueAsync(connection, protector.Unprotect(connection.EncryptedToken),
            draft);

        var link = new FindingIssueLink
        {
            VulnerabilityId = findingId,
            ConnectionId = connectionId,
            IssueKey = issue.Key,
            IssueId = issue.Id,
            IssueUrl = issue.Url,
            LastSyncedStatus = issue.Status,
            LastSyncAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        db.FindingIssueLinks.Add(link);
        await db.SaveChangesAsync();

        Logger.Information("Finding {Finding} linked to {Provider} issue {Issue} on connection {Connection}",
            findingId, connection.Provider, issue.Key, connection.Name);

        link.Connection = connection;
        return ToView(link);
    }

    public async Task<List<FindingIssueLinkView>> CreateIssuesAsync(int connectionId,
        IReadOnlyList<int> findingIds, int? userId)
    {
        var created = new List<FindingIssueLinkView>();

        foreach (var findingId in findingIds.Distinct())
        {
            try
            {
                created.Add(await CreateIssueAsync(connectionId, findingId, userId));
            }
            catch (Exception ex)
            {
                // Per finding rather than all-or-nothing: a multi-selection of forty findings where the
                // eleventh is rejected should still file the other thirty-nine, and the operator needs
                // to know which one failed.
                Logger.Warning("Could not create an issue for finding {Finding} on connection {Connection}: {Message}",
                    findingId, connectionId, ex.Message);
            }
        }

        return created;
    }

    public async Task<FindingIssueLinkView> LinkExistingAsync(int connectionId, int findingId,
        string issueKeyOrUrl, int? userId)
    {
        if (string.IsNullOrWhiteSpace(issueKeyOrUrl))
            throw new InvalidParameterException(nameof(issueKeyOrUrl), "An issue key or URL is required.");

        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, connectionId);
        await LoadFindingAsync(db, findingId);

        var provider = ProviderFor(connection);
        var key = ExtractKey(connection.Provider, issueKeyOrUrl);

        // Read it before linking: a link to an issue that does not exist is a link that fails silently
        // on every subsequent sync.
        var issue = await provider.GetIssueAsync(connection, protector.Unprotect(connection.EncryptedToken), key)
                    ?? throw new DataNotFoundException("issue", key,
                        new Exception($"{connection.Name} has no issue '{key}'."));

        var existing = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .FirstOrDefaultAsync(l => l.ConnectionId == connectionId && l.IssueKey == issue.Key);

        if (existing != null)
        {
            if (existing.VulnerabilityId != findingId)
                throw new InvalidParameterException(nameof(issueKeyOrUrl),
                    $"Issue {issue.Key} is already linked to finding #{existing.VulnerabilityId}.");

            return ToView(existing);
        }

        var link = new FindingIssueLink
        {
            VulnerabilityId = findingId,
            ConnectionId = connectionId,
            IssueKey = issue.Key,
            IssueId = issue.Id,
            IssueUrl = issue.Url,
            LastSyncedStatus = issue.Status,
            LastSyncAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        db.FindingIssueLinks.Add(link);
        await db.SaveChangesAsync();

        link.Connection = connection;
        return ToView(link);
    }

    public async Task UnlinkAsync(int linkId)
    {
        await using var db = DalService.GetContext();

        var link = await db.FindingIssueLinks.FirstOrDefaultAsync(l => l.Id == linkId)
                   ?? throw new DataNotFoundException("finding_issue_links", linkId.ToString(),
                       new Exception($"Link {linkId} was not found."));

        db.FindingIssueLinks.Remove(link);
        await db.SaveChangesAsync();
    }

    // --- synchronization --------------------------------------------------------------------

    public async Task<IssueSyncResult> PollConnectionAsync(int connectionId, int? userId = null)
    {
        var result = new IssueSyncResult();

        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, connectionId);
        var provider = ProviderFor(connection);
        var token = protector.Unprotect(connection.EncryptedToken);

        var links = await db.FindingIssueLinks
            .Where(l => l.ConnectionId == connectionId)
            .ToListAsync();

        foreach (var link in links)
        {
            result.Examined++;

            try
            {
                var issue = await provider.GetIssueAsync(connection, token, link.IssueKey);

                if (issue == null)
                {
                    // Recorded rather than deleted: a deleted ticket is something the operator should
                    // see, and unlinking on their behalf loses the evidence that it ever existed.
                    link.SyncError = "The issue no longer exists in the tracker.";
                    link.LastSyncAt = DateTime.UtcNow;
                    result.Errors++;
                    continue;
                }

                var applied = await ApplyIssueStateAsync(db, connection, link, issue, userId);

                if (applied.Changed) result.Changed++;
                if (applied.Applied) result.Applied++;
                if (applied.Conflict) result.Conflicts++;
                if (applied.Message != null) result.Messages.Add(applied.Message);
            }
            catch (Exception ex)
            {
                link.SyncError = Truncate(ex.Message, 1000);
                link.LastSyncAt = DateTime.UtcNow;
                result.Errors++;
                result.Messages.Add($"{link.IssueKey}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync();

        await RecordSyncAsync(IntegrationKind.IssueTracker, connection.Id, connection.Name, result);

        return result;
    }

    public async Task<IssueSyncResult> PollDueConnectionsAsync(DateTime nowUtc)
    {
        var combined = new IssueSyncResult();

        List<int> due;

        await using (var db = DalService.GetContext())
        {
            // "Due" is decided from the shared sync log rather than a column on the connection, so a
            // connection polled by hand also resets the clock.
            var lastRuns = await db.IntegrationSyncLogs
                .Where(l => l.Integration == IntegrationKind.IssueTracker && l.ConnectionId != null)
                .GroupBy(l => l.ConnectionId!.Value)
                .Select(g => new { ConnectionId = g.Key, Last = g.Max(l => l.StartedAt) })
                .ToDictionaryAsync(x => x.ConnectionId, x => x.Last);

            var connections = await db.IssueTrackerConnections
                .Where(c => c.Enabled)
                .Select(c => new { c.Id, c.PollIntervalMinutes })
                .ToListAsync();

            due = connections
                .Where(c => !lastRuns.TryGetValue(c.Id, out var last)
                            || last.AddMinutes(Math.Max(1, c.PollIntervalMinutes)) <= nowUtc)
                .Select(c => c.Id)
                .ToList();
        }

        foreach (var connectionId in due)
        {
            try
            {
                var result = await PollConnectionAsync(connectionId);
                combined.Examined += result.Examined;
                combined.Changed += result.Changed;
                combined.Applied += result.Applied;
                combined.Conflicts += result.Conflicts;
                combined.Errors += result.Errors;
                combined.Messages.AddRange(result.Messages);
            }
            catch (Exception ex)
            {
                combined.Errors++;
                Logger.Warning("Polling issue-tracker connection {Id} failed: {Message}", connectionId, ex.Message);
            }
        }

        return combined;
    }

    public async Task<IssueSyncResult> ApplyWebhookAsync(int connectionId, string rawBody,
        IReadOnlyDictionary<string, string> headers, string? presentedSecret)
    {
        var result = new IssueSyncResult();

        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, connectionId);
        var provider = ProviderFor(connection);
        var secret = protector.Unprotect(connection.EncryptedWebhookSecret);

        // Providers that cannot sign a body (Jira, Azure DevOps) carry the shared secret in the URL,
        // so it is compared here before the payload is parsed at all.
        if (!provider.Capabilities.SupportsWebhooks)
            throw new InvalidParameterException(nameof(connectionId),
                $"{provider.Name} does not support inbound webhooks.");

        if (RequiresUrlSecret(connection.Provider))
        {
            // Track 7 finding NR-2026-019: this was an ordinary string comparison, which returns as
            // soon as two characters differ and so leaks the secret one character at a time to a
            // caller who can measure the response. The signed providers already compared in constant
            // time; the unsigned ones, which rely on this secret alone, did not.
            if (string.IsNullOrEmpty(secret) || !FixedTimeEquals(presentedSecret, secret))
            {
                Logger.Warning("A webhook for connection {Connection} presented the wrong URL secret",
                    connection.Name);
                throw new WebhookAuthenticationException(provider.Name);
            }
        }

        var issue = provider.ParseWebhook(connection, secret, rawBody, headers);

        if (issue == null || string.IsNullOrEmpty(issue.Key))
        {
            // Not an error: a signature that fails is refused by the provider (and logged there), and a
            // payload that is not an issue event is simply not interesting.
            Logger.Debug("Webhook for connection {Connection} carried nothing actionable", connection.Name);
            return result;
        }

        var link = await db.FindingIssueLinks
            .FirstOrDefaultAsync(l => l.ConnectionId == connectionId && l.IssueKey == issue.Key);

        if (link == null)
        {
            Logger.Debug("Webhook for connection {Connection} named untracked issue {Issue}",
                connection.Name, issue.Key);
            return result;
        }

        result.Examined = 1;

        var applied = await ApplyIssueStateAsync(db, connection, link, issue, null);

        if (applied.Changed) result.Changed = 1;
        if (applied.Applied) result.Applied = 1;
        if (applied.Conflict) result.Conflicts = 1;
        if (applied.Message != null) result.Messages.Add(applied.Message);

        await db.SaveChangesAsync();

        return result;
    }

    /// <summary>
    /// Compares two secrets without leaking their contents through timing.
    ///
    /// The length check before it is not a leak worth worrying about: the length of a
    /// NetRisk-generated webhook secret is fixed and public.
    /// </summary>
    private static bool FixedTimeEquals(string? presented, string expected)
    {
        if (presented == null) return false;

        var a = System.Text.Encoding.UTF8.GetBytes(presented);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);

        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// The core of inbound sync: compare the tracker's state to what was last seen, look up the
    /// action, and apply it to the finding.
    /// </summary>
    private async Task<(bool Changed, bool Applied, bool Conflict, string? Message)> ApplyIssueStateAsync(
        AuditableContext db, IssueTrackerConnection connection, FindingIssueLink link,
        ExternalIssue issue, int? userId)
    {
        link.LastSyncAt = DateTime.UtcNow;
        link.SyncError = null;
        link.IssueUrl ??= issue.Url;

        var status = issue.Status ?? string.Empty;

        if (string.Equals(link.LastSyncedStatus, status, StringComparison.OrdinalIgnoreCase))
            return (false, false, false, null);

        var previous = link.LastSyncedStatus;
        link.LastSyncedStatus = status;
        link.LastChangeFromRemote = true;

        var mapping = await db.IssueStatusMappings
            .Where(m => m.ConnectionId == connection.Id)
            .ToListAsync();

        var action = mapping.FirstOrDefault(m =>
            string.Equals(m.ExternalStatus, status, StringComparison.OrdinalIgnoreCase))?.Action;

        // No explicit mapping: a closed issue still means something, so the tracker's own
        // "is this closed" flag is honoured as MarkMitigated. An operator who disagrees maps that
        // status to None explicitly.
        if (action == null && issue.IsClosed) action = IssueSyncAction.MarkMitigated;

        if (action is null or IssueSyncAction.None)
            return (true, false, false, $"{link.IssueKey}: '{previous}' → '{status}' (no action mapped)");

        var finding = await db.Vulnerabilities.FirstOrDefaultAsync(v => v.Id == link.VulnerabilityId);

        if (finding == null)
            return (true, false, false, $"{link.IssueKey}: the linked finding no longer exists");

        var target = TargetStatusFor(action.Value);

        if (target != null && finding.LifecycleStatus == target)
            return (true, false, false, $"{link.IssueKey}: the finding is already {target}");

        // Conflict detection: NetRisk moved the finding to a suppressed or resolved state of its own
        // accord, and the tracker is now asking for something different. Last-writer-wins is applied
        // (the tracker just spoke), and the row is flagged so a human sees that it happened.
        var conflict = target != null
                       && finding.LifecycleStatus != FindingStatus.Active
                       && finding.LifecycleStatus != FindingStatus.Verified
                       && finding.LifecycleStatus != target;

        if (conflict)
        {
            link.HasConflict = true;
            link.ConflictDetail =
                $"NetRisk had the finding as {finding.LifecycleStatus}; {connection.Name} {link.IssueKey} "
                + $"moved to '{status}', which maps to {action}. The tracker's change was applied.";
        }

        try
        {
            // Save the link state first: if the transition throws (an illegal move, say), the record
            // that the tracker changed must survive, otherwise the next poll re-applies it forever.
            await db.SaveChangesAsync();

            if (action == IssueSyncAction.ScheduleReverify)
            {
                // Deliberately does not transition the finding. "Re-verify" means a human or a scanner
                // has to confirm the fix, and moving it to Mitigated first is precisely the assumption
                // the option exists to avoid.
                await notifications.IssueSyncAppliedAsync(finding, connection.Name, link.IssueKey, status,
                    action.Value);

                return (true, true, conflict,
                    $"{link.IssueKey}: '{previous}' → '{status}' — re-verification requested for finding #{finding.Id}");
            }

            await lifecycle.TransitionAsync(finding.Id, target!.Value, userId,
                FindingStatusChangeSource.IssueSync,
                $"{connection.Name} {link.IssueKey} moved to '{status}'.");

            await notifications.IssueSyncAppliedAsync(finding, connection.Name, link.IssueKey, status,
                action.Value);

            return (true, true, conflict,
                $"{link.IssueKey}: '{previous}' → '{status}' — finding #{finding.Id} set to {target}");
        }
        catch (Exception ex)
        {
            link.SyncError = Truncate($"Could not apply {action}: {ex.Message}", 1000);
            return (true, false, conflict, $"{link.IssueKey}: {ex.Message}");
        }
    }

    private static FindingStatus? TargetStatusFor(IssueSyncAction action) => action switch
    {
        IssueSyncAction.MarkMitigated => FindingStatus.Mitigated,
        IssueSyncAction.MarkFalsePositive => FindingStatus.FalsePositive,
        IssueSyncAction.Reactivate => FindingStatus.Active,
        _ => null
    };

    public async Task<int> PushFindingTransitionAsync(int findingId, FindingStatus to, string? note = null)
    {
        var pushed = 0;

        await using var db = DalService.GetContext();

        var links = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .ThenInclude(c => c!.StatusMappings)
            .Where(l => l.VulnerabilityId == findingId)
            .ToListAsync();

        foreach (var link in links)
        {
            var connection = link.Connection;

            if (connection is not { Enabled: true, PushFindingUpdates: true }) continue;

            // Loop protection. The finding only reached this state because the tracker asked for it, so
            // pushing it back would post a comment that the tracker reports as a change, which comes
            // back in as another inbound sync.
            if (link.LastChangeFromRemote)
            {
                link.LastChangeFromRemote = false;
                Logger.Debug("Not pushing finding {Finding} to {Issue}: the change originated there",
                    findingId, link.IssueKey);
                continue;
            }

            var provider = registry.For(connection.Provider);
            if (provider == null) continue;

            // The outbound half of the same mapping table, matched on the NetRisk status name.
            var transition = connection.StatusMappings
                ?.FirstOrDefault(m => TargetStatusFor(m.Action) == to)
                ?.OutboundTransition;

            var comment = note ?? $"NetRisk set finding #{findingId} to {to}.";

            try
            {
                await provider.UpdateIssueAsync(connection, protector.Unprotect(connection.EncryptedToken),
                    link.IssueKey, comment,
                    provider.Capabilities.SupportsTransitions ? transition : null);

                link.LastSyncAt = DateTime.UtcNow;
                link.SyncError = null;
                pushed++;
            }
            catch (Exception ex)
            {
                // Recorded, not thrown: a tracker that refuses a comment must not fail the NetRisk
                // transition that triggered it.
                link.SyncError = Truncate(ex.Message, 1000);
                Logger.Warning("Could not push finding {Finding} to {Issue}: {Message}",
                    findingId, link.IssueKey, ex.Message);
            }
        }

        await db.SaveChangesAsync();

        return pushed;
    }

    public async Task<List<FindingIssueLinkView>> ApplyAutoCreatePolicyAsync(int findingId, int? userId = null)
    {
        var created = new List<FindingIssueLinkView>();

        List<int> connectionIds;
        int? severity;
        int? entityId;

        await using (var db = DalService.GetContext())
        {
            var finding = await db.Vulnerabilities.FirstOrDefaultAsync(v => v.Id == findingId);
            if (finding == null) return created;

            severity = Notifications.NotificationEventPublisher.SeverityFromFinding(finding);
            entityId = finding.EntityId;

            if (severity == null) return created;

            var candidates = await db.IssueTrackerConnections
                .Where(c => c.Enabled && c.AutoCreateMinSeverity != null)
                .Select(c => new { c.Id, c.AutoCreateMinSeverity, c.EntityId })
                .ToListAsync();

            connectionIds = candidates
                .Where(c => severity >= c.AutoCreateMinSeverity
                            && (c.EntityId == null || c.EntityId == entityId))
                .Select(c => c.Id)
                .ToList();
        }

        foreach (var connectionId in connectionIds)
        {
            try
            {
                created.Add(await CreateIssueAsync(connectionId, findingId, userId));
            }
            catch (Exception ex)
            {
                Logger.Warning("Auto-create on connection {Connection} failed for finding {Finding}: {Message}",
                    connectionId, findingId, ex.Message);
            }
        }

        return created;
    }

    public async Task<List<FindingIssueLinkView>> GetConflictsAsync()
    {
        await using var db = DalService.GetContext();

        var links = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .Where(l => l.HasConflict)
            .OrderByDescending(l => l.LastSyncAt)
            .ToListAsync();

        return links.Select(ToView).ToList();
    }

    public async Task<FindingIssueLinkView> ResolveConflictAsync(int linkId)
    {
        await using var db = DalService.GetContext();

        var link = await db.FindingIssueLinks
                       .Include(l => l.Connection)
                       .FirstOrDefaultAsync(l => l.Id == linkId)
                   ?? throw new DataNotFoundException("finding_issue_links", linkId.ToString(),
                       new Exception($"Link {linkId} was not found."));

        link.HasConflict = false;
        link.ConflictDetail = null;

        await db.SaveChangesAsync();

        return ToView(link);
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// Jira and Azure DevOps cannot sign a webhook body, so their authenticity check is a shared
    /// secret in the receiver URL. GitHub and GitLab authenticate the delivery itself and do not need
    /// one.
    /// </summary>
    internal static bool RequiresUrlSecret(IssueTrackerProviderKind provider) =>
        provider is IssueTrackerProviderKind.Jira or IssueTrackerProviderKind.AzureDevOps;

    internal IssueDraft BuildDraft(IssueTrackerConnection connection, Vulnerability finding, string? assetName)
    {
        var link = BaseUrl == null ? null : $"{BaseUrl}/vulnerabilities/{finding.Id}";
        var values = IssueTemplate.ValuesFor(finding, link, assetName);

        var labels = (connection.DefaultLabels ?? string.Empty)
            .Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new IssueDraft
        {
            Title = IssueTemplate.Render(
                string.IsNullOrWhiteSpace(connection.TitleTemplate)
                    ? IssueTemplate.DefaultTitle
                    : connection.TitleTemplate, values),
            Description = IssueTemplate.Render(
                string.IsNullOrWhiteSpace(connection.DescriptionTemplate)
                    ? IssueTemplate.DefaultDescription
                    : connection.DescriptionTemplate, values),
            Priority = ResolvePriority(connection, finding),
            Labels = labels,
            IssueType = connection.IssueType,
            FindingId = finding.Id
        };
    }

    /// <summary>
    /// The connection's severity→priority map, falling back to the provider default. Parsed leniently:
    /// a malformed mapping means no priority rather than a failed issue creation.
    /// </summary>
    internal static string? ResolvePriority(IssueTrackerConnection connection, Vulnerability finding)
    {
        var severity = Notifications.NotificationEventPublisher.SeverityFromFinding(finding);
        if (severity == null) return null;

        if (!string.IsNullOrWhiteSpace(connection.PriorityMappingJson))
        {
            try
            {
                var configured = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    connection.PriorityMappingJson);

                if (configured != null && configured.TryGetValue(severity.Value.ToString(), out var mapped)
                                       && !string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }
            catch (JsonException)
            {
                // Falls through to the default rather than failing the create.
            }
        }

        return DefaultPriorities.TryGetValue(connection.Provider, out var defaults)
            ? defaults.GetValueOrDefault(severity.Value)
            : null;
    }

    /// <summary>
    /// Pulls the issue key out of whatever the operator pasted. Per provider because the key's shape
    /// differs: Jira's is <c>PROJ-123</c> anywhere in a URL, the others' is the last path segment.
    /// </summary>
    internal static string ExtractKey(IssueTrackerProviderKind provider, string input)
    {
        var trimmed = input.Trim();

        if (provider == IssueTrackerProviderKind.Jira)
        {
            var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"[A-Z][A-Z0-9_]+-\d+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpperInvariant() : trimmed;
        }

        return provider switch
        {
            IssueTrackerProviderKind.GitHub => GitHubIssueTrackerProvider.Number(trimmed),
            IssueTrackerProviderKind.GitLab => GitLabIssueTrackerProvider.Iid(trimmed),
            _ => AzureDevOpsIssueTrackerProvider.Id(trimmed)
        };
    }

    private void Validate(IssueTrackerConnection connection)
    {
        if (connection == null) throw new InvalidParameterException(nameof(connection), "A connection is required.");

        if (string.IsNullOrWhiteSpace(connection.Name))
            throw new InvalidParameterException(nameof(connection.Name), "A connection requires a name.");

        if (registry.For(connection.Provider) == null)
            throw new InvalidParameterException(nameof(connection.Provider),
                $"No provider is registered for {connection.Provider}. Available: "
                + string.Join(", ", registry.All.Select(p => p.Kind)));

        if (string.IsNullOrWhiteSpace(connection.BaseUrl)
            || !Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidParameterException(nameof(connection.BaseUrl),
                "The base URL must be an absolute http or https URL.");

        if (string.IsNullOrWhiteSpace(connection.ProjectKey))
            throw new InvalidParameterException(nameof(connection.ProjectKey),
                "A connection requires a project (Jira project key, owner/repo, GitLab path, or ADO project).");

        if (connection.AutoCreateMinSeverity is < 1 or > 4)
            throw new InvalidParameterException(nameof(connection.AutoCreateMinSeverity),
                "Auto-create severity must be between 1 (Low) and 4 (Critical), or unset for manual only.");
    }

    private IIssueTrackerProvider ProviderFor(IssueTrackerConnection connection) =>
        registry.For(connection.Provider)
        ?? throw new InvalidParameterException(nameof(connection.Provider),
            $"No provider is registered for {connection.Provider}.");

    private static async Task<IssueTrackerConnection> LoadAsync(AuditableContext db, int id) =>
        await db.IssueTrackerConnections.Include(c => c.StatusMappings).FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new DataNotFoundException("issue_tracker_connections", id.ToString(),
            new Exception($"Issue-tracker connection {id} was not found."));

    private static async Task<Vulnerability> LoadFindingAsync(AuditableContext db, int id) =>
        await db.Vulnerabilities.FirstOrDefaultAsync(v => v.Id == id)
        ?? throw new DataNotFoundException("vulnerabilities", id.ToString(),
            new Exception($"Finding {id} was not found."));

    private static async Task<string?> AssetNameAsync(AuditableContext db, Vulnerability finding)
    {
        if (finding.HostId == null) return null;

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == finding.HostId);
        return host?.HostName ?? host?.Fqdn ?? host?.Ip;
    }

    private async Task RecordSyncAsync(IntegrationKind kind, int connectionId, string connectionName,
        IssueSyncResult result)
    {
        await using var db = DalService.GetContext();

        db.IntegrationSyncLogs.Add(new IntegrationSyncLog
        {
            Integration = kind,
            ConnectionId = connectionId,
            ConnectionName = connectionName,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = result.Errors == 0
                ? IntegrationSyncStatus.Succeeded
                : result.Applied > 0 || result.Changed > 0
                    ? IntegrationSyncStatus.PartiallySucceeded
                    : IntegrationSyncStatus.Failed,
            UpdatedCount = result.Applied,
            SkippedCount = result.Examined - result.Changed,
            FailedCount = result.Errors,
            Summary = Truncate(
                $"{result.Examined} link(s) examined, {result.Changed} changed, {result.Applied} applied, "
                + $"{result.Conflicts} conflict(s)."
                + (result.Messages.Count == 0 ? "" : " " + string.Join(" | ", result.Messages.Take(20))), 2000)
        });

        await db.SaveChangesAsync();
    }

    private IssueTrackerConnectionView ToView(IssueTrackerConnection connection) => new()
    {
        Id = connection.Id,
        Name = connection.Name,
        Provider = connection.Provider,
        BaseUrl = connection.BaseUrl,
        ProjectKey = connection.ProjectKey,
        IssueType = connection.IssueType,
        AuthUser = connection.AuthUser,
        HasToken = !string.IsNullOrEmpty(connection.EncryptedToken),
        HasWebhookSecret = !string.IsNullOrEmpty(connection.EncryptedWebhookSecret),
        PriorityMappingJson = connection.PriorityMappingJson,
        TitleTemplate = connection.TitleTemplate,
        DescriptionTemplate = connection.DescriptionTemplate,
        DefaultLabels = connection.DefaultLabels,
        EntityId = connection.EntityId,
        Enabled = connection.Enabled,
        AutoCreateMinSeverity = connection.AutoCreateMinSeverity,
        PushFindingUpdates = connection.PushFindingUpdates,
        PollIntervalMinutes = connection.PollIntervalMinutes,
        StatusMappings = (connection.StatusMappings ?? new List<IssueStatusMapping>())
            .OrderBy(m => m.ExternalStatus)
            .Select(m => new IssueStatusMappingView
            {
                Id = m.Id,
                ExternalStatus = m.ExternalStatus,
                Action = m.Action,
                OutboundTransition = m.OutboundTransition
            }).ToList(),
        Capabilities = registry.For(connection.Provider)?.Capabilities
    };

    private static FindingIssueLinkView ToView(FindingIssueLink link) => new()
    {
        Id = link.Id,
        FindingId = link.VulnerabilityId,
        ConnectionId = link.ConnectionId,
        ConnectionName = link.Connection?.Name ?? string.Empty,
        Provider = link.Connection?.Provider ?? default,
        IssueKey = link.IssueKey,
        IssueUrl = link.IssueUrl,
        LastSyncedStatus = link.LastSyncedStatus,
        LastSyncAt = link.LastSyncAt,
        SyncError = link.SyncError,
        HasConflict = link.HasConflict,
        ConflictDetail = link.ConflictDetail
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
