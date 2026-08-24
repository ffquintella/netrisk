using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;

namespace ClientServices.Interfaces;

/// <summary>
/// Per-entity role assignments (Track 2 milestone 2.3.2). A user can hold a different role in
/// each business entity, and assignments are revoked rather than deleted so "who could access
/// what on date T" stays answerable.
/// </summary>
public interface IUserAccessService
{
    /// <summary>The user's currently active assignments. Revoked rows are not returned.</summary>
    Task<List<UserEntityRole>> GetUserEntityRolesAsync(int userId);

    Task<UserEntityRole> AssignEntityRoleAsync(int userId, int entityId, int roleId);

    /// <summary>Soft-revokes the assignment, preserving it for audit.</summary>
    Task RevokeEntityRoleAsync(int assignmentId);
}
