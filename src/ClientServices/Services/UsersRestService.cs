using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ClientServices.Events;
using Model.Exceptions;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Users;
using RestSharp;

namespace ClientServices.Services;

public class UsersRestService: RestServiceBase, IUsersService
{
    
    private List<UserListing> _cachedUserListings = new ();
    private bool _fullCache = false;
    
    public UsersRestService(IRestService restService): base(restService)
    {
        UserAdded += (_, _) => { };
    }

    public string GetUserName(int id)
    {
        if(_fullCache)
        {
            var user = _cachedUserListings.FirstOrDefault(u => u.Id == id);
            if(user != null) return user.Name;
        }
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/Name/{id}");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                Logger.Error("Error getting risks");
                response = "";
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting user name message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting user name", ex);
        }
    }

    public async Task<string> GetUserNameAsync(int id)
    {
        if(_fullCache)
        {
            var user = _cachedUserListings.FirstOrDefault(u => u.Id == id);
            if(user != null) return user.Name;
        }
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/Name/{id}");
        
        try
        {
            var response = await client.GetAsync<string>(request);

            if (response == null)
            {
                Logger.Error("Error getting risks");
                response = "";
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting user name message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting user name", ex);
        }
    }

    public List<UserListing> ListUsers()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/Listings");
        try
        {
            var response = client.Get<List<UserListing>>(request);

            if (response == null)
            {
                Logger.Error("Error listing users");
                throw new InvalidHttpRequestException("Error listing users", "/Users/Listings", "GET");
            }
            
            _fullCache = true;
            _cachedUserListings = response.OrderBy(ul => ul.Name).ToList();
            
            return _cachedUserListings;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing users message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing users", ex);
        }
    }

    public async Task<List<UserListing>> GetAllAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/Listings");
        try
        {
            var response = await client.GetAsync<List<UserListing>>(request);

            if (response == null)
            {
                Logger.Error("Error listing users");
                throw new InvalidHttpRequestException("Error listing users", "/Users/Listings", "GET");
            }
            
            _fullCache = true;
            _cachedUserListings = response.OrderBy(ul => ul.Name).ToList();
            
            return _cachedUserListings;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing users message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing users", ex);
        }
    }

    public void LoadCache()
    {
        if(_fullCache) return;
        ListUsers();
    }
    
    public UserDto CreateUser(UserDto user)
    {
        
        if(user.Id != 0)
            throw new ArgumentException("User ID must be 0");
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users");
        try
        {
            request.AddJsonBody(user);
            
            var response = client.Post(request);
            
            if (response == null || response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error saving user");
                throw new InvalidHttpRequestException("Error getting user", "/Users/{id}", "GET");
            }
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var newUser = JsonSerializer.Deserialize<UserDto>(response.Content!, options);
            
            if(newUser == null) throw new Exception("Error deserializing user");
            
            var ul = new UserListing
            {
                Id = newUser.Id,
                Name = newUser.Name
            };
            NotifyUserAdded(new UserAddedEventArgs
            {
                User = ul
            });
            return newUser;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating user message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating users", ex);
        }
        

    }



    public void SaveUser(UserDto user)
    {
        
        if(user.Id == 0)
            throw new ArgumentException("User cannot be 0");
        
        if(string.IsNullOrEmpty(user.Lang)) user.Lang = "en";
            
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{user.Id}");
        try
        {
            request.AddJsonBody(user);
            
            var response = client.Put(request);
            
            if (response == null || response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error saving user");
                throw new InvalidHttpRequestException("Error getting user", "/Users/{id}", "GET");
            }
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error saving user message:{Message}", ex.Message);
            throw new RestComunicationException("Error saving users", ex);
        }
    }

    public UserDto GetUser(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{id}");
        try
        {
            var response = client.Get<UserDto>(request);

            if (response == null)
            {
                Logger.Error("Error getting user");
                throw new InvalidHttpRequestException("Error getting user", "/Users/{id}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting user message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing users", ex);
        }
    }

    public async Task<UserDto> GetUserAsync(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{id}");
        try
        {
            var response = await client.GetAsync<UserDto>(request);

            if (response == null)
            {
                Logger.Error("Error getting user");
                throw new InvalidHttpRequestException("Error getting user", "/Users/{id}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting user message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing users", ex);
        }
    }

    public void DeleteUser(int userId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{userId}");
        try
        {
            var response = client.Delete(request);

            if (response == null || response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting user");
                throw new InvalidHttpRequestException("Error deleting user", "/Users/{id}", "Delete");
            }
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting user message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting users", ex);
        }
    }

    public List<Permission> GetAllPermissions()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/Permissions");
        try
        {
            var response = client.Get<List<Permission>>(request);

            if (response == null)
            {
                Logger.Error("Error getting permissions");
                throw new InvalidHttpRequestException("Error getting permissions", "/Users/permissions", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting permissions message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing permissions", ex);
        }
    }

    public void ChangePassword(int userId, string password)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{userId}/ChangePassword");
        
        var pwd = new ChangePasswordRequest()
        {
            NewPassword = password
        };

        request.AddJsonBody(pwd);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error changing password");
                throw new InvalidHttpRequestException("Error changing password", $"/Users/{userId}/ChangePassword", "POST");
            }

            return;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error changing password message:{Message}", ex.Message);
            throw new RestComunicationException("Error changing password", ex);
        }
    }

    public List<Permission> GetUserPermissions(int userId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{userId}/Permissions");
        try
        {
            var response = client.Get<List<Permission>>(request);

            if (response == null)
            {
                Logger.Error("Error getting user permissions");
                throw new InvalidHttpRequestException("Error getting permissions", $"/Users/{userId}/permissions", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting user permissions message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing permissions", ex);
        }
    }

    public void SaveUserPermissions(int userId, List<Permission?> permissions)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Users/{userId}/Permissions");

        var pintList = permissions.Where(p=> p != null ).Select(p => p!.Id).ToList();

        request.AddJsonBody(pintList);
        
        try
        {
            var response = client.Put(request);

            if (response == null || response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error saving user permissions");
                throw new InvalidHttpRequestException("Error saving permissions", $"/Users/{userId}/permissions", "PUT");
            }

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error saving user permissions message:{Message}", ex.Message);
            throw new RestComunicationException("Error saving permissions", ex);
        } 
    }
    
    private void NotifyUserAdded(UserAddedEventArgs ua)
    {
        EventHandler<UserAddedEventArgs> handler = UserAdded;
        handler(this, ua);
    }
    public event EventHandler<UserAddedEventArgs> UserAdded;
    
}