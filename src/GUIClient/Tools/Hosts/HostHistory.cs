using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using DAL.Enums;

namespace GUIClient.Tools.Hosts;

/// <summary>One field-level change inside a save, or the summary row of a create/delete.</summary>
public sealed record HostHistoryChange(AuditLogAction Action, string Field, string? OldValue, string? NewValue)
{
    /// <summary>
    /// A create or delete row: the interceptor writes it with an empty field and the whole record
    /// dumped into <see cref="NewValue"/> (create) or <see cref="OldValue"/> (delete).
    /// </summary>
    public bool IsSummary => Action != AuditLogAction.Update || string.IsNullOrEmpty(Field);

    /// <summary>The record dump a summary row carries, whichever side it is on.</summary>
    public string? Snapshot => Action == AuditLogAction.Delete ? OldValue : NewValue ?? OldValue;
}

/// <summary>Every row one save wrote: one actor, one moment (S38 §5.4).</summary>
public sealed record HostHistoryGroup(string Key, string Actor, DateTime OccurredAtUtc,
    IReadOnlyList<HostHistoryChange> Changes);

/// <summary>One line of a History group as the tab renders it: a label and what happened.</summary>
public sealed record HostHistoryLine(string Label, string Detail);

/// <summary>One save as the History tab renders it: <c>actor · when</c>, then its lines.</summary>
public sealed record HostHistoryEntry(string Header, IReadOnlyList<HostHistoryLine> Lines);

/// <summary>
/// Turns <c>GET /Hosts/{id}/History</c> (newest first, one row per changed field) into what the
/// History tab lists: groups by <c>CorrelationId</c>, filtered by field, actor and a start date.
///
/// Jira's known weakness is an unfiltered list (S38 §2); the three filters are the answer to "who
/// changed this host's criticality, and when", which is the question this tab exists for.
/// </summary>
public static class HostHistory
{
    /// <summary>
    /// The resource key that names a <c>Host</c> property in the user's language, for the fields a
    /// host save can touch. A field not listed reads as its property name — still true, just not
    /// translated — so a column added later does not render blank.
    /// </summary>
    private static readonly Dictionary<string, string> FieldLabelKeys = new(StringComparer.Ordinal)
    {
        [nameof(Host.HostName)] = "HostName",
        [nameof(Host.Ip)] = "IP",
        [nameof(Host.Fqdn)] = "FQDN",
        [nameof(Host.MacAddress)] = "MacAddress",
        [nameof(Host.Os)] = "OperatingSystem",
        [nameof(Host.OsVersion)] = "OsVersion",
        [nameof(Host.Status)] = "Status",
        [nameof(Host.TeamId)] = "ResponsibleTeam",
        [nameof(Host.Comment)] = "Comment",
        [nameof(Host.Source)] = "Source",
        [nameof(Host.RegistrationDate)] = "RegistrationDate",
        [nameof(Host.Criticality)] = "Criticality",
        [nameof(Host.Environment)] = "Environment",
        [nameof(Host.Owner)] = "Owner",
        [nameof(Host.RiskScore)] = "RiskScore",
        [nameof(Host.RiskScoreSource)] = "RiskScoreSource",
        [nameof(Host.ExternalId)] = "ExternalId",
        [nameof(Host.ExternalProvider)] = "ExternalProvider",
        [nameof(Host.EntityId)] = "Entity",
    };

    /// <summary>Every resource key <see cref="FieldLabelKey"/> can return.</summary>
    public static IEnumerable<string> AllFieldLabelKeys => FieldLabelKeys.Values;

    /// <summary>The resource key for a field, or null when the field has none.</summary>
    public static string? FieldLabelKey(string field) =>
        FieldLabelKeys.TryGetValue(field, out var key) ? key : null;

    /// <summary>The resource key for a create/delete/update summary label.</summary>
    public static string ActionLabelKey(AuditLogAction action) => action switch
    {
        AuditLogAction.Create => "AuditActionCreate",
        AuditLogAction.Delete => "AuditActionDelete",
        _ => "AuditActionUpdate"
    };

    /// <summary><c>old → new</c>, a dash standing in for an empty side.</summary>
    public static string Transition(string? oldValue, string? newValue) =>
        (string.IsNullOrEmpty(oldValue) ? "–" : oldValue) + " → " + (string.IsNullOrEmpty(newValue) ? "–" : newValue);

    /// <summary>The distinct fields the trail names, ordered — the Field filter's options.</summary>
    public static IReadOnlyList<string> Fields(IEnumerable<AuditLog> entries) =>
        entries.Select(e => e.Field)
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>The distinct actors the trail names, ordered — the Actor filter's options.</summary>
    public static IReadOnlyList<string> Actors(IEnumerable<AuditLog> entries) =>
        entries.Select(e => e.Actor)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The groups to show, newest first.
    ///
    /// A row with no correlation id (written before the interceptor stamped one) is a group of its
    /// own rather than being merged with every other uncorrelated row. A field filter keeps only the
    /// matching rows of each group, and drops a group left empty; an actor filter and a start date
    /// apply to the whole group.
    /// </summary>
    /// <param name="entries">The trail as the server returned it.</param>
    /// <param name="field">Keep only changes to this field; null or empty for all.</param>
    /// <param name="actor">Keep only saves by this actor; null or empty for all.</param>
    /// <param name="sinceUtc">Keep only saves at or after this instant (UTC); null for all.</param>
    public static IReadOnlyList<HostHistoryGroup> Group(IEnumerable<AuditLog> entries, string? field = null,
        string? actor = null, DateTime? sinceUtc = null)
    {
        var groups = new List<HostHistoryGroup>();

        foreach (var save in entries.GroupBy(e => string.IsNullOrEmpty(e.CorrelationId)
                     ? "row:" + e.Id
                     : e.CorrelationId!))
        {
            var rows = save.ToList();
            var occurredAt = rows.Max(r => AsUtc(r.OccurredAt));
            var by = rows.Select(r => r.Actor).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? string.Empty;

            if (!string.IsNullOrEmpty(actor) && !string.Equals(by, actor, StringComparison.Ordinal)) continue;
            if (sinceUtc is { } since && occurredAt < AsUtc(since)) continue;

            var changes = rows
                .Where(r => string.IsNullOrEmpty(field) || string.Equals(r.Field, field, StringComparison.Ordinal))
                .OrderBy(r => r.Action == AuditLogAction.Update ? 1 : 0)
                .ThenBy(r => r.Field, StringComparer.Ordinal)
                .Select(r => new HostHistoryChange(r.Action, r.Field ?? string.Empty, r.OldValue, r.NewValue))
                .ToList();

            if (changes.Count == 0) continue;

            groups.Add(new HostHistoryGroup(save.Key, by, occurredAt, changes));
        }

        return groups.OrderByDescending(g => g.OccurredAtUtc).ToList();
    }

    /// <summary>
    /// Renders groups for display. An update line reads <c>Field   old → new</c>, the field named in
    /// the user's language; a create/delete line carries the action as its label and the record
    /// dump as its detail, since that row has no field to name.
    /// </summary>
    /// <param name="groups">From <see cref="Group"/>.</param>
    /// <param name="localize">Resource key → text.</param>
    /// <param name="formatValue">Field and stored value → what to show (a team id as its name, …).</param>
    /// <param name="formatTime">UTC instant → the user's local time, formatted.</param>
    public static IReadOnlyList<HostHistoryEntry> Present(IEnumerable<HostHistoryGroup> groups,
        Func<string, string> localize, Func<string, string?, string?> formatValue, Func<DateTime, string> formatTime) =>
        groups.Select(group => new HostHistoryEntry(
                HostSummaryLine.Join(group.Actor, formatTime(group.OccurredAtUtc)),
                group.Changes.Select(change => change.IsSummary
                        ? new HostHistoryLine(localize(ActionLabelKey(change.Action)), change.Snapshot ?? string.Empty)
                        : new HostHistoryLine(
                            FieldLabelKey(change.Field) is { } key ? localize(key) : change.Field,
                            Transition(formatValue(change.Field, change.OldValue),
                                formatValue(change.Field, change.NewValue))))
                    .ToList()))
            .ToList();

    /// <summary>
    /// The server writes UTC; JSON without an offset deserialises as Unspecified, which is read as
    /// UTC rather than reinterpreted as local time.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
