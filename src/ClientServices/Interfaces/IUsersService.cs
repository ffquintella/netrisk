using System;
using System.Collections.Generic;
using ClientServices.Events;
using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IUsersService
{
    /// <summary>
    /// Loads the cache
    /// </summary>
    void LoadCache();
    
    /// <summary>
    /// Gets the user name for a given ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    string GetUserName(int id);

    /// <summary>
    /// Gets the user name for a given ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<string> GetUserNameAsync(int id);
    
    /// <summary>
    /// List all users
    /// </summary>
    /// <returns></returns>
    public List<UserListing> ListUsers();
    
    /// <summary>
    /// List all users
    /// </summary>
    /// <returns></returns>
    public Task<List<UserListing>> GetAllAsync();
    
    /// <summary>
    /// Called when a new user is added
    /// </summary>
    public event EventHandler<UserAddedEventArgs> UserAdded;
    
    /// <summary>
    /// Creates a new user
    /// </summary>
    /// <param name="user"></param>
    public UserDto CreateUser(UserDto user);
    
    /// <summary>
    /// Updates a user
    /// </summary>
    /// <param name="user"></param>
    public void SaveUser(UserDto user);
    
    /// <summary>
    /// Deletes a single user
    /// </summary>
    /// <param name="userId"></param>
    public void DeleteUser(int userId);
    
    /// <summary>
    /// Gets a user by it´s ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public UserDto GetUser(int id);
    
    /// <summary>
    /// Gets a user by it´s ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public  Task<UserDto> GetUserAsync(int id);
    
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
    
    /// <summary>
    /// Saves the permissions for a user
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="permissions"></param>
    public void SaveUserPermissions(int userId, List<Permission?> permissions);
    
    /// <summary>
    /// Changes the password for a user
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="password"></param>
    public void ChangePassword(int userId, string password);

}