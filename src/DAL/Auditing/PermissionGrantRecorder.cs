using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DAL.Auditing;

/// <summary>
/// Writes a <see cref="PermissionGrantEvent"/> for every change in a save that can put a watched permission in force
/// (S40 §6.4, §11.4, §11.7): a watched permission added to or removed from a role or a user, a user moving between roles,
/// a role created or renamed, and an edit of <c>risk_index_approver_roles</c>. Called by <see cref="AuditableContext"/>
/// before the save and never swallowed, so whatever path made the change — roles, users, SCIM, an identity provider —
/// cannot commit it without its record. <c>PermissionGrantRecorderTest</c> covers each kind and the insert-only rule.
/// </summary>
public static class PermissionGrantRecorder
{
    /// <summary>The permissions S40 §6.4 names (<c>RiskIndexPermissions</c>, <c>EntityPermissions.RiskContext</c> agree).</summary>
    public static readonly IReadOnlySet<string> WatchedPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        "risk_index_view",
        "risk_index_configure",
        "risk_index_approve",
        "entity_risk_context",
        "business_risk_review"
    };

    /// <summary>The skip-navigation join entities EF tracks for <c>Role.Permissions</c> and <c>User.Permissions</c>.</summary>
    public const string RoleJoin = "RoleResponsibility", UserJoin = "PermissionToUser";

    /// <summary>The role names a value of <c>risk_index_approver_roles</c> lists, as the approval parses them.</summary>
    public static IReadOnlySet<string> ApproverRoleNames(string? value) =>
        (value ?? RiskIndexSettingKeys.DefaultApproverRoles)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Call after <c>DetectChanges</c>: the recorder reads the tracker as it stands and never detects again.</summary>
    public static void Record(AuditableContext context)
    {
        // The context has just detected changes; a second pass over a large import's graph would only cost time.
        var tracker = context.ChangeTracker;
        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            RecordDetected(context);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private static void RecordDetected(AuditableContext context)
    {
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (entries.Count == 0) return;

        EnsureInsertOnly(entries);

        var actorId = context.UserId > 0 ? context.UserId : (int?)null;
        var actor = string.IsNullOrEmpty(context.AuditActor) ? AuditableContext.SystemActor : context.AuditActor;
        actor = actor.Length > 64 ? actor[..64] : actor;
        var now = DateTime.UtcNow;
        var events = new List<PermissionGrantEvent>();

        PermissionRows(context, entries, events);
        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case User user:
                    Membership(context, entry, user, events);
                    break;
                case Role role:
                    RoleName(entry, role, events);
                    break;
                case Setting setting:
                    ApproverRoles(entry, setting, events);
                    break;
            }
        }

        foreach (var e in events)
        {
            e.ActorId = actorId;
            e.Actor = actor;
            e.CreatedAt = now;
            context.Add(e);
        }
    }

    /// <summary>
    /// The record is evidence: an update or a delete of it is refused. The one exception is EF's own SET NULL of the role or
    /// user when that role or user is deleted — what the database does to untracked rows too.
    /// </summary>
    private static void EnsureInsertOnly(IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries.Where(e => e.Entity is PermissionGrantEvent))
        {
            if (entry.State == EntityState.Deleted ||
                (entry.State == EntityState.Modified && entry.Properties.Any(p => p.IsModified &&
                    !(p.Metadata.Name is nameof(PermissionGrantEvent.RoleId) or nameof(PermissionGrantEvent.UserId) &&
                      p.CurrentValue is null))))
                throw new InvalidOperationException("permission_grant_events is insert-only.");
        }
    }

    private static void PermissionRows(AuditableContext context, IReadOnlyList<EntityEntry> entries, List<PermissionGrantEvent> events)
    {
        var rows = entries.Where(e => e.State is EntityState.Added or EntityState.Deleted &&
                                      e.Metadata.Name is RoleJoin or UserJoin).ToList();
        if (rows.Count == 0) return;

        var ids = rows.Select(r => Int(r, "PermissionId")).ToHashSet();
        var keys = context.ChangeTracker.Entries<Permission>().Where(p => ids.Contains(Key(p, nameof(Permission.Id))))
            .GroupBy(p => Key(p, nameof(Permission.Id))).ToDictionary(g => g.Key, g => g.First().Entity.Key);
        var missing = ids.Where(i => !keys.ContainsKey(i)).ToList();
        if (missing.Count > 0)
            foreach (var p in context.Permissions.AsNoTracking().Where(p => missing.Contains(p.Id)).Select(p => new { p.Id, p.Key }).ToList())
                keys[p.Id] = p.Key;

        foreach (var row in rows)
        {
            if (!keys.TryGetValue(Int(row, "PermissionId"), out var key) || !WatchedPermissions.Contains(key)) continue;
            var action = row.State == EntityState.Added ? PermissionGrantAction.Grant : PermissionGrantAction.Revoke;
            var e = new PermissionGrantEvent { Action = action, PermissionKey = key };
            if (row.Metadata.Name == RoleJoin)
            {
                e.Kind = PermissionGrantKind.RolePermission;
                LinkRole(context, e, Int(row, "RoleId"));
            }
            else
            {
                e.Kind = PermissionGrantKind.UserPermission;
                LinkUser(context, e, Int(row, "UserId"));
            }

            events.Add(e);
        }
    }

    /// <summary>Joining a role puts every grant of the role in force, today's and any added later: always recorded.</summary>
    private static void Membership(AuditableContext context, EntityEntry entry, User user, List<PermissionGrantEvent> events)
    {
        // Read from the entry, not the entity: a role created in the same save has a temporary key, which EF keeps in the
        // change tracker until the insert — it is a role all the same.
        var property = entry.Property(nameof(User.RoleId));
        var current = (int)property.CurrentValue!;
        var hasRole = current > 0 || (current != 0 && property.IsTemporary);

        if (entry.State == EntityState.Added)
        {
            if (hasRole) events.Add(MembershipEvent(context, PermissionGrantAction.Grant, user, entry, current, null));
            return;
        }

        if (entry.State != EntityState.Modified) return;
        if (!property.IsModified || property.OriginalValue is not int before || before == current) return;

        if (before > 0) events.Add(MembershipEvent(context, PermissionGrantAction.Revoke, user, entry, before, current));
        if (hasRole) events.Add(MembershipEvent(context, PermissionGrantAction.Grant, user, entry, current, before));
    }

    private static PermissionGrantEvent MembershipEvent(AuditableContext context, PermissionGrantAction action, User user,
        EntityEntry entry, int roleId, int? other)
    {
        var e = new PermissionGrantEvent
        {
            Kind = PermissionGrantKind.RoleMembership, Action = action,
            OldValue = action == PermissionGrantAction.Grant ? Text(other) : Text(roleId),
            NewValue = action == PermissionGrantAction.Grant ? Text(roleId) : Text(other)
        };
        if (entry.Property(nameof(User.Value)).IsTemporary) e.User = user;
        else e.UserId = (int)entry.Property(nameof(User.Value)).CurrentValue!;
        LinkRole(context, e, roleId);
        return e;
    }

    /// <summary>A role created or renamed: the name is what the setting matches, so a rename into it is a grant.</summary>
    private static void RoleName(EntityEntry entry, Role role, List<PermissionGrantEvent> events)
    {
        string? before;
        if (entry.State == EntityState.Added) before = null;
        else if (entry.State == EntityState.Modified && entry.Property(nameof(Role.Name)).IsModified)
        {
            before = entry.Property(nameof(Role.Name)).OriginalValue as string;
            if (string.Equals(before, role.Name, StringComparison.Ordinal)) return;
        }
        else return;

        var e = new PermissionGrantEvent
        {
            Kind = PermissionGrantKind.RoleName, Action = PermissionGrantAction.Grant, RoleName = Truncate(role.Name),
            OldValue = before, NewValue = role.Name
        };
        if (entry.State == EntityState.Added || entry.Property(nameof(Role.Value)).IsTemporary) e.Role = role;
        else e.RoleId = Key(entry, nameof(Role.Value));
        events.Add(e);
    }

    /// <summary>One event per role name the edit adds or removes; a deleted row falls back to the default list.</summary>
    private static void ApproverRoles(EntityEntry entry, Setting setting, List<PermissionGrantEvent> events)
    {
        if (!string.Equals(setting.Name, RiskIndexSettingKeys.ApproverRoles, StringComparison.Ordinal)) return;

        var value = entry.Property(nameof(Setting.Value));
        string? before = entry.State == EntityState.Added ? null : value.OriginalValue as string;
        string? after = entry.State == EntityState.Deleted ? null : setting.Value;
        if (entry.State == EntityState.Modified && string.Equals(before, after, StringComparison.Ordinal)) return;

        var old = ApproverRoleNames(before);
        var now = ApproverRoleNames(after);
        foreach (var name in now.Except(old).Order(StringComparer.Ordinal))
            events.Add(SettingEvent(PermissionGrantAction.Grant, name, before, after));
        foreach (var name in old.Except(now).Order(StringComparer.Ordinal))
            events.Add(SettingEvent(PermissionGrantAction.Revoke, name, before, after));
    }

    private static PermissionGrantEvent SettingEvent(PermissionGrantAction action, string name, string? before, string? after) => new()
    {
        Kind = PermissionGrantKind.ApproverRoles, Action = action, RoleName = Truncate(name), OldValue = before, NewValue = after
    };

    /// <summary>
    /// Points the event at the role: by navigation when the role is new in this save (its key is not assigned yet), by id
    /// otherwise — and by name only when the role is being deleted, since a new row cannot reference it.
    /// </summary>
    private static void LinkRole(AuditableContext context, PermissionGrantEvent e, int roleId)
    {
        var tracked = context.ChangeTracker.Entries<Role>().FirstOrDefault(r => Key(r, nameof(Role.Value)) == roleId);
        if (tracked is null)
        {
            e.RoleId = roleId;
            return;
        }

        e.RoleName = Truncate(tracked.Entity.Name);
        if (tracked.State == EntityState.Deleted) e.OldValue ??= Text(roleId);
        else if (tracked.State == EntityState.Added || tracked.Property(nameof(Role.Value)).IsTemporary) e.Role = tracked.Entity;
        else e.RoleId = roleId;
    }

    private static void LinkUser(AuditableContext context, PermissionGrantEvent e, int userId)
    {
        var tracked = context.ChangeTracker.Entries<User>().FirstOrDefault(u => Key(u, nameof(User.Value)) == userId);
        if (tracked is null) e.UserId = userId;
        else if (tracked.State == EntityState.Deleted) e.OldValue ??= Text(userId);
        else if (tracked.State == EntityState.Added || tracked.Property(nameof(User.Value)).IsTemporary) e.User = tracked.Entity;
        else e.UserId = userId;
    }

    private static int Int(EntityEntry entry, string property)
    {
        var p = entry.Property(property);
        return (int)(entry.State == EntityState.Deleted ? p.OriginalValue! : p.CurrentValue!);
    }

    /// <summary>A key as the change tracker holds it: the temporary value of a row not inserted yet, else the entity's.</summary>
    private static int Key(EntityEntry entry, string property) => (int)entry.Property(property).CurrentValue!;

    /// <summary>An id as text; a temporary (negative) key is no id anyone can look up, so it is left out.</summary>
    private static string? Text(int? value) =>
        value is > 0 ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    private static string? Truncate(string? value) => value is { Length: > 100 } ? value[..100] : value;
}
