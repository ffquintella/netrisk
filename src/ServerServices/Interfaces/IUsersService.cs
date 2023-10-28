using System;
using System.Collections.Generic;
using DAL.Entities;
using Model.DTO;

namespace ServerServices.Interfaces;

public interface IUsersService
{
    User? GetUser(string userName);
    List<string> GetUserPermissions(int userId);

    String GetUserName(int id);
    
    List<UserListing> ListActiveUsers();
    
    bool VerifyPassword(string username, string password);
    bool VerifyPassword(int userId, string password);
    
    bool VerifyPassword(User? user, string password);
    
    bool ChangePassword(int userId, string password);

    /// <summary>
    /// Get a user by their id
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public User? GetUserById(int userId);
    
    /// <summary>
    /// Saves the user to the database
    /// </summary>
    /// <param name="user"></param>
    public void SaveUser(User user);
    
    /// <summary>
    /// Registers a login for a user
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="ipAddress"></param>
    public void RegisterLogin(int userId, string ipAddress);
    
    /// <summary>
    /// Deletes one user by their id
    /// </summary>
    /// <param name="userId"></param>
    public void DeleteUser(int userId);
    
    /// <summary>
    /// Creates a new user
    /// </summary>
    /// <param name="user"></param>
    public User CreateUser(User user);
    
    /// <summary>
    /// Checks if the password is complex enough
    /// </summary>
    /// <param name="password"></param>
    /// <returns></returns>
    public bool CheckPasswordComplexity(string password);

}