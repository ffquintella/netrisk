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
    /// Save the permissions for a user by id
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="permissions"></param>
    public void SaveUserPermissionsById(int userId, List<int> permissions);
    
    /// <summary>
    /// List all permissions
    /// </summary>
    /// <returns></returns>
    public List<Permission> GetAllPermissions();
    
    
    /// <summary>
    /// Gets the default user permissions
    /// </summary>
    /// <returns></returns>
    public List<Permission> GetDefaultPermissions();
    
    
    /// <summary>
    ///  Gets a permission by its key
    /// </summary>
    /// <param name="permissionKey"></param>
    /// <returns></returns>
    public Permission GetByKey(string permissionKey);
    

}