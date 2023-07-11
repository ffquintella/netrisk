using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IPermissionsService
{
    /// <summary>
    /// Check if the user has a permission
    /// </summary>
    /// <param name="user"></param>
    /// <param name="Permission"></param>
    /// <returns></returns>
    public bool UserHasPermission(User user, string Permission);
    
    /// <summary>
    /// Gets the list of permissions to a user
    /// </summary>
    /// <param name="user"></param>
    /// <returns></returns>
    public List<string> GetUserPermissions(User user);
    
    /// <summary>
    /// Get´s the list of permissions to a user by id
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public List<Permission> GetUserPermissionsById(int userId);
    
    /// <summary>
    /// List all permissions
    /// </summary>
    /// <returns></returns>
    public List<Permission> GetAllPermissions();

}