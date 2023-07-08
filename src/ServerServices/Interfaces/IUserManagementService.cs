using System;
using System.Collections.Generic;
using DAL.Entities;
using Model.DTO;

namespace ServerServices.Interfaces;

public interface IUserManagementService
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
    /// Creates a new user
    /// </summary>
    /// <param name="user"></param>
    public User CreateUser(User user);

}