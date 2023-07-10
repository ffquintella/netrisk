using ClientServices.Events;
using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IUsersService
{
    string GetUserName(int id);

    List<UserListing> ListUsers();
    
    /// <summary>
    /// Called when a new user is added
    /// </summary>
    public event EventHandler<UserAddedEventArgs> UserAdded;
    
    
    /// <summary>
    /// Creates a new user
    /// </summary>
    /// <param name="user"></param>
    public void AddUser(User user);
    
    /// <summary>
    /// Gets a user by it´s ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public UserDto GetUser(int id);

}