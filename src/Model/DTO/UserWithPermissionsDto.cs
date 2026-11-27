using System.Collections.Generic;

namespace Model.DTO;

/// <summary>
/// The body of <c>PUT /Users/{id}/with-permissions</c>: a user and the ids of the permissions granted to them directly,
/// saved together in one transaction.
/// </summary>
public class UserWithPermissionsDto
{
    public UserDto User { get; set; } = new();

    /// <summary>Every permission the user holds directly once saved; one left out is revoked.</summary>
    public List<int> PermissionIds { get; set; } = new();
}
