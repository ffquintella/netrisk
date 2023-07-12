using ClientServices.Events;
using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IUsersService
{
    string GetUserName(int id);

    /// <summary>
    /// List all users
    /// </summary>
    /// <returns></returns>
    public List<UserListing> ListUsers();
    
    /// <summary>
    /// Called when a new user is added
    /// </summary>
    public event EventHandler<UserAddedEventArgs> UserAdded;
    
    
    /// <summary>
    /// Creates a new user
    /// </summary>
    /// <param name="user"></param>
    public void CreateUser(UserDto user);
    
    /// <summary>
    /// Updates a user
    /// </summary>
    /// <param name="user"></param>
    public void SaveUser(UserDto user);
    
    /// <summary>
    /// Gets a user by it´s ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public UserDto GetUser(int id);
    
    /// <summary>
    /// Gets a list of permissions
    /// </summary>
    /// <returns></returns>
    public List<Permission> GetAllPermissions();
    
    /// <summary>
    /// Gets a list of permissions for a user
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public List<Permission> GetUserPermissions(int userId);

}