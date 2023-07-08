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
    
    public void AddUser(User user);

}