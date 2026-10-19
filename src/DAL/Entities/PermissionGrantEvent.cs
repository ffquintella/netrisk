using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One change that can put a watched permission in force for somebody, and who made it (S40 §6.4, §11.4, §11.7) —
/// <c>permission_grant_events</c>. Written by <c>AuditableContext</c> in the same save as the change; insert-only; read by
/// the profile approval's <c>sod_self_granted_approver</c>. Not in <c>audit_logs</c>, whose retention would let an
/// administrator wait the check out.
/// </summary>
public class PermissionGrantEvent
{
    public long Id { get; set; }

    public PermissionGrantKind Kind { get; set; }

    public PermissionGrantAction Action { get; set; }

    /// <summary>The role granted, joined or left, renamed or created. Null for a direct grant or once the role is deleted.</summary>
    public int? RoleId { get; set; }

    /// <summary>The role name the event concerns: the setting lists names, not ids.</summary>
    public string? RoleName { get; set; }

    /// <summary>The user granted directly or moved between roles. Null for a role-level change or once the user is deleted.</summary>
    public int? UserId { get; set; }

    /// <summary>The permission key of a role or direct grant.</summary>
    public string? PermissionKey { get; set; }

    /// <summary>The previous value: the setting's text, the role's previous name or the previous role id.</summary>
    public string? OldValue { get; set; }

    /// <summary>The new value, in the same terms as <see cref="OldValue"/>.</summary>
    public string? NewValue { get; set; }

    /// <summary>Who made the change; null for the job host, the console or a migration. No foreign key: it outlives its author.</summary>
    public int? ActorId { get; set; }

    /// <summary>The audit actor name of the save (<c>AuditableContext.AuditActor</c>).</summary>
    public string Actor { get; set; } = null!;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual Role? Role { get; set; }

    public virtual User? User { get; set; }
}
