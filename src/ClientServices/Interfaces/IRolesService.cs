using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IRolesService
{
    /// <summary>
    /// Gets all roles
    /// </summary>
    /// <returns></returns>
    public List<Role> GetAllRoles();
    public Task<List<Role>> GetAllRolesAsync();
    
    /// <summary>
    ///  Deletes a role
    /// </summary>
    /// <param name="roleId"></param>
    public void Delete(int roleId);
    
    /// <summary>
    /// Creates a new Role
    /// </summary>
    /// <param name="role"></param>
    /// <returns></returns>
    public Role Create(Role role);
    
    /// <summary>
    ///  Gets the role permission names
    /// </summary>
    /// <param name="roleId"></param>
    /// <returns></returns>
    public List<string> GetRolePermissions(int roleId);
    
    /// <summary>
    ///  Updates the role permissions
    /// </summary>
    /// <param name="roleId"></param>
    /// <param name="permissions"></param>
    public void UpdateRolePermissions(int roleId, List<string> permissions);
}