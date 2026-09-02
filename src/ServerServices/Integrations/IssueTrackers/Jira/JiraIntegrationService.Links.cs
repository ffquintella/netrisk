using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// Issue links for records other than findings (Track 4 milestone 4.6): incidents and risks.
///
/// Findings keep going through <c>IIssueTrackerService</c>, which has the lifecycle transitions, the
/// auto-create policy and the conflict queue. This adds the two record kinds that have none of that
/// and need none of it: a link to an incident or a risk is a *reference*, mirrored and displayed, and
/// nothing about it transitions the NetRisk record on its own. Wiring "Done" onto closing an incident
/// would be a policy decision nobody has specified, and this repository has shipped documented
/// controls that did not work three times already.
/// </summary>
public partial class JiraIntegrationService
{
    public async Task<List<FindingIssueLinkView>> GetLinksForRecordAsync(IssueLinkTargetKind targetKind,
        int targetId)
    {
        await using var db = DalService.GetContext();

        var links = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .Where(l => l.TargetKind == targetKind
                        && (targetKind == IssueLinkTargetKind.Finding
                            ? l.VulnerabilityId == targetId
                            : targetKind == IssueLinkTargetKind.Incident
                                ? l.IncidentId == targetId
                                : l.RiskId == targetId))
            .OrderBy(l => l.IssueKey)
            .ToListAsync();

        return links.Select(ToView).ToList();
    }

    public async Task<FindingIssueLinkView> CreateIssueForRecordAsync(int connectionId,
        IssueLinkTargetKind targetKind, int targetId, int? userId)
    {
        if (targetKind == IssueLinkTargetKind.Finding)
            throw new InvalidParameterException(nameof(targetKind),
                "Findings are created through the finding-issues endpoint, which also applies the "
                + "auto-create policy and the conflict queue.");

        var (connection, token, _) = await ResolveAsync(connectionId);

        await using var db = DalService.GetContext();

        var existing = await FindLinkAsync(db, connectionId, targetKind, targetId);

        // Idempotent per (connection, record), like the finding path: pressing the button twice must
        // return the ticket that exists rather than filing a duplicate in somebody else's project.
        if (existing != null) return ToView(existing);

        var draft = await BuildDraftAsync(db, connection, targetKind, targetId);

        var issue = await JiraProvider().CreateIssueAsync(connection, token, draft);

        var link = new FindingIssueLink
        {
            ConnectionId = connectionId,
            IssueKey = issue.Key,
            IssueId = issue.Id,
            IssueUrl = issue.Url,
            LastSyncedStatus = issue.Status,
            LastSyncAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        link.SetTarget(targetKind, targetId);

        Guard(link);

        db.FindingIssueLinks.Add(link);
        await db.SaveChangesAsync();

        Logger.Information(
            "User:{User} created Jira issue {Issue} for {Kind} {Target} on connection {Connection}",
            userId, issue.Key, targetKind, targetId, connectionId);

        link.Connection = connection;

        return ToView(link);
    }

    public async Task<FindingIssueLinkView> LinkRecordAsync(int connectionId,
        IssueLinkTargetKind targetKind, int targetId, string issueKeyOrUrl, int? userId)
    {
        var (connection, token, _) = await ResolveAsync(connectionId);

        var key = ExtractKey(issueKeyOrUrl);

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidParameterException(nameof(issueKeyOrUrl),
                "Give an issue key (SD-4711) or the issue's URL.");

        // Read before the link is stored. A link to an issue that does not exist fails on every later
        // sync instead of failing here, where somebody is watching and can fix the typo.
        var issue = await JiraProvider().GetIssueAsync(connection, token, key)
                    ?? throw new DataNotFoundException("issue", key,
                        new Exception($"{connection.Name} has no issue '{key}'."));

        await using var db = DalService.GetContext();

        var duplicate = await db.FindingIssueLinks
            .Include(l => l.Connection)
            .FirstOrDefaultAsync(l => l.ConnectionId == connectionId && l.IssueKey == issue.Key);

        if (duplicate != null)
        {
            if (duplicate.TargetKind == targetKind && duplicate.TargetId == targetId)
                return ToView(duplicate);

            throw new InvalidParameterException(nameof(issueKeyOrUrl),
                $"Issue {issue.Key} is already linked to {duplicate.TargetKind} #{duplicate.TargetId}.");
        }

        await EnsureTargetExistsAsync(db, targetKind, targetId);

        var link = new FindingIssueLink
        {
            ConnectionId = connectionId,
            IssueKey = issue.Key,
            IssueId = issue.Id,
            IssueUrl = issue.Url,
            LastSyncedStatus = issue.Status,
            LastSyncAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        link.SetTarget(targetKind, targetId);

        Guard(link);

        db.FindingIssueLinks.Add(link);
        await db.SaveChangesAsync();

        Logger.Information("User:{User} linked {Kind} {Target} to {Issue} on connection {Connection}",
            userId, targetKind, targetId, issue.Key, connectionId);

        link.Connection = connection;

        return ToView(link);
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// The registered Jira provider, for the two calls that actually write to Jira.
    ///
    /// Resolved from the registry rather than constructed here, so this path shares the ADF
    /// conversion, the 255-character summary truncation and the label rules that milestone 4.2 already
    /// got right — a second Jira writer would have had to rediscover all three.
    /// </summary>
    private IIssueTrackerProvider JiraProvider() =>
        registry.For(IssueTrackerProviderKind.Jira)
        ?? throw new IntegrationRequestException("Jira",
            "No Jira issue-tracker provider is registered in this host.");

    /// <summary>
    /// Refuses a link whose target columns and discriminator disagree.
    ///
    /// The schema has a <c>CHECK</c> for the same invariant, but a constraint the application can trip
    /// surfaces as a database exception with no useful message. This is the guard that produces a
    /// sentence; the constraint is the backstop for a code path that forgot to call it.
    /// </summary>
    private static void Guard(FindingIssueLink link)
    {
        if (link.Validate() is { } problem)
            throw new InvalidParameterException(nameof(link), problem);
    }

    private static Task<FindingIssueLink?> FindLinkAsync(AuditableContext db, int connectionId,
        IssueLinkTargetKind targetKind, int targetId) =>
        db.FindingIssueLinks
            .Include(l => l.Connection)
            .FirstOrDefaultAsync(l => l.ConnectionId == connectionId
                                      && l.TargetKind == targetKind
                                      && (targetKind == IssueLinkTargetKind.Incident
                                          ? l.IncidentId == targetId
                                          : l.RiskId == targetId));

    private static async Task EnsureTargetExistsAsync(AuditableContext db,
        IssueLinkTargetKind targetKind, int targetId)
    {
        var exists = targetKind switch
        {
            IssueLinkTargetKind.Finding => await db.Vulnerabilities.AnyAsync(v => v.Id == targetId),
            IssueLinkTargetKind.Incident => await db.Incidents.AnyAsync(i => i.Id == targetId),
            IssueLinkTargetKind.Risk => await db.Risks.AnyAsync(r => r.Id == targetId),
            _ => false
        };

        // The entity-scope query filters apply to these reads, so a target in another business
        // entity is simply not found — which is the right answer and not a leak of its existence.
        if (!exists)
            throw new DataNotFoundException(targetKind.ToString().ToLowerInvariant(),
                targetId.ToString(),
                new Exception($"No {targetKind} {targetId} is visible to this caller."));
    }

    /// <summary>
    /// Renders the issue for an incident or a risk.
    ///
    /// Each kind gets its own values because they genuinely differ: a risk has no CVE and an incident
    /// has no CVSS, and one shared placeholder set would mean half the table in every ticket is blank.
    /// The connection's title and description templates still apply, so an operator who customised
    /// them gets their format for these too.
    /// </summary>
    private async Task<IssueDraft> BuildDraftAsync(AuditableContext db,
        IssueTrackerConnection connection, IssueLinkTargetKind targetKind, int targetId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string title;

        if (targetKind == IssueLinkTargetKind.Incident)
        {
            var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == targetId)
                           ?? throw new DataNotFoundException("incident", targetId.ToString(),
                               new Exception($"No incident {targetId} is visible to this caller."));

            title = string.IsNullOrWhiteSpace(incident.Name)
                ? $"Incident #{incident.Id}"
                : incident.Name;

            values["FindingId"] = incident.Id.ToString();
            values["Title"] = title;
            values["Status"] = ((Model.IntStatus)incident.Status).ToString();
            values["Description"] = incident.Description ?? string.Empty;
            values["Severity"] = "Incident";
            values["Link"] = LinkTo($"/incidents/{incident.Id}", $"incident #{incident.Id}");
        }
        else
        {
            var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == targetId)
                       ?? throw new DataNotFoundException("risk", targetId.ToString(),
                           new Exception($"No risk {targetId} is visible to this caller."));

            title = risk.Subject;

            values["FindingId"] = risk.Id.ToString();
            values["Title"] = title;
            values["Status"] = risk.Status;
            // Notes rather than Assessment: the notes are the narrative a person wrote about the
            // risk, and the assessment is the scoring rationale, which reads as noise in a ticket.
            values["Description"] = risk.Notes;
            values["Severity"] = "Risk";
            values["Link"] = LinkTo($"/risks/{risk.Id}", $"risk #{risk.Id}");
        }

        // Anything the caller's kind does not have resolves to empty rather than being left as a
        // visible {{Placeholder}}: an operator's finding template referencing {{Cvss}} should not put
        // the literal text "{{Cvss}}" in an incident ticket.
        foreach (var field in MappableFields.IssueSourceFields)
            values.TryAdd(field, string.Empty);

        var body = Render(connection.DescriptionTemplate, values);

        return new IssueDraft
        {
            Title = Render(connection.TitleTemplate ?? "[{{Severity}}] {{Title}}", values),
            Description = string.IsNullOrWhiteSpace(body)
                ? $"{values["Severity"]} — {title}\n\n{values["Description"]}\n\n{values["Link"]}"
                : body,
            IssueType = connection.IssueType,
            Labels = (connection.DefaultLabels ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            FindingId = targetId
        };
    }

    private static string Render(string? template, IReadOnlyDictionary<string, string> values) =>
        string.IsNullOrWhiteSpace(template)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(template, @"\{\{(\w+)\}\}",
                match => values.TryGetValue(match.Groups[1].Value, out var value)
                    ? value
                    : string.Empty);

    private string LinkTo(string route, string label) =>
        BaseUrl == null ? string.Empty : $"[Open {label} in NetRisk]({BaseUrl}{route})";

    /// <summary>
    /// The key out of a key or a browse URL. Jira keys are <c>ABC-123</c>, so the last path segment of
    /// a URL is the key and a bare key passes through unchanged.
    /// </summary>
    internal static string ExtractKey(string keyOrUrl)
    {
        var text = keyOrUrl.Trim();

        if (!text.Contains('/')) return text;

        var segment = text.TrimEnd('/').Split('/').LastOrDefault() ?? text;

        // A query string on a browse URL (?filter=…) would otherwise become part of the key.
        return segment.Split('?')[0];
    }

    private static FindingIssueLinkView ToView(FindingIssueLink link) => new()
    {
        Id = link.Id,
        TargetKind = link.TargetKind,
        TargetId = link.TargetId,
        FindingId = link.VulnerabilityId ?? 0,
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
}
