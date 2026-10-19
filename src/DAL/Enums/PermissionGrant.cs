namespace DAL.Enums;

/// <summary>
/// What a <c>permission_grant_events</c> row records (S40 §6.4, §11.4, §11.7): each way a watched permission can come into
/// force for a user. Values are stored and only ever appended.
/// </summary>
public enum PermissionGrantKind
{
    /// <summary>A watched permission added to or removed from a role (<c>role_responsibilities</c>).</summary>
    RolePermission = 1,

    /// <summary>A watched permission granted to or removed from a user directly (<c>permission_to_user</c>).</summary>
    UserPermission = 2,

    /// <summary>A user moved into (<see cref="PermissionGrantAction.Grant"/>) or out of a role (<c>user.role_id</c>).</summary>
    RoleMembership = 3,

    /// <summary>A role name added to or removed from <c>risk_index_approver_roles</c>.</summary>
    ApproverRoles = 4,

    /// <summary>A role created or renamed: its name is what <c>risk_index_approver_roles</c> lists.</summary>
    RoleName = 5
}

/// <summary>Whether the change put the grant in force or took it away.</summary>
public enum PermissionGrantAction
{
    Grant = 1,
    Revoke = 2
}
