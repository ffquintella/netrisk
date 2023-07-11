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
    /// Gets one role
    /// </summary>
    /// <param name="roleId"></param>
    /// <returns></returns>
    public Role? GetRole(int roleId);
    
    /// <summary>
    /// List all roles
    /// </summary>
    /// <returns></returns>
    public List<Role> GetRoles();
}