using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IRolesService
{
    /// <summary>
    /// Gets the role permissions
    /// </summary>
    /// <param name="roleId"></param>
    /// <returns></returns>
    public List<string> GetRolePermissions(int roleId);
    
    /// <summary>
    /// Gets the role permissions
    /// </summary>
    /// <param name="roleId"></param>
    /// <returns></returns>
    public Task<List<string>> GetRolePermissionsAsync(int roleId);
    
    /// <summary>
    /// Gets one role
    /// </summary>
    /// <param name="roleId"></param>
    /// <returns></returns>
    public Role? GetRole(int roleId);
    
    /// <summary>
    /// Gets one role async
    /// </summary>
    /// <param name="roleId"></param>
    /// <returns></returns>
    public Task<Role?> GetRoleAsync(int roleId);
    
    /// <summary>
    /// List all roles
    /// </summary>
    /// <returns></returns>
    public List<Role> GetRoles();
    
    
    /// <summary>
    /// Updates the permissions for a role
    /// </summary>
    /// <param name="roleId"></param>
    /// <param name="permissions"></param>
    /// <returns></returns>
    public void UpdatePermissions(int roleId, List<string> permissions);
    
    /// <summary>
    /// Updates the permissions for a role
    /// </summary>
    /// <param name="roleId"></param>
    /// <param name="permissions"></param>
    /// <returns></returns>
    public Task UpdatePermissionsAsync(int roleId, List<string> permissions);
    
    /// <summary>
    ///  Creates a new role
    /// </summary>
    /// <param name="role"></param>
    /// <returns></returns>
    public Role? CreateRole(Role role);
    
    
    /// <summary>
    /// Deletes a role
    /// </summary>
    /// <param name="roleId"></param>
    public void DeleteRole(int roleId);
}