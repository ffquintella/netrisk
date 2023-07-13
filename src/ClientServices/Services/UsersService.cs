using System.Net;
using System.Text.Json;
using ClientServices.Events;
using Model.Exceptions;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using RestSharp;

namespace ClientServices.Services;

public class UsersService: ServiceBase, IUsersService
{
    
    public UsersService(IRestService restService): base(restService)
    {
        UserAdded += (_, _) => { };
    }

    public string GetUserName(int id)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users/Name/{id}");
        
        try
        {
            var response = client.Get<string>(request);

            if (response == null)
            {
                _logger.Error("Error getting risks");
                response = "";
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting user name message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting user name", ex);
        }
    }

    public List<UserListing> ListUsers()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users/Listings");
        try
        {
            var response = client.Get<List<UserListing>>(request);

            if (response == null)
            {
                _logger.Error("Error listing users");
                throw new InvalidHttpRequestException("Error listing users", "/Users/Listings", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error listing users message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing users", ex);
        }
    }

    public UserDto CreateUser(UserDto user)
    {
        
        if(user.Id != 0)
            throw new ArgumentException("User ID must be 0");
        
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users");
        try
        {
            request.AddJsonBody(user);
            
            var response = client.Post(request);
            
            if (response == null || response.StatusCode != HttpStatusCode.OK)
            {
                _logger.Error("Error saving user");
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
            _logger.Error("Error creating user message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating users", ex);
        }
        

    }

    public void SaveUser(UserDto user)
    {
        
        if(user.Id == 0)
            throw new ArgumentException("User cannot be 0");
        
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users/{user.Id}");
        try
        {
            request.AddJsonBody(user);
            
            var response = client.Put(request);
            
            if (response == null || response.StatusCode != HttpStatusCode.OK)
            {
                _logger.Error("Error saving user");
                throw new InvalidHttpRequestException("Error getting user", "/Users/{id}", "GET");
            }
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error saving user message:{Message}", ex.Message);
            throw new RestComunicationException("Error saving users", ex);
        }
    }

    public UserDto GetUser(int id)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users/{id}");
        try
        {
            var response = client.Get<UserDto>(request);

            if (response == null)
            {
                _logger.Error("Error getting user");
                throw new InvalidHttpRequestException("Error getting user", "/Users/{id}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting user message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing users", ex);
        }
    }

    public List<Permission> GetAllPermissions()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users/Permissions");
        try
        {
            var response = client.Get<List<Permission>>(request);

            if (response == null)
            {
                _logger.Error("Error getting permissions");
                throw new InvalidHttpRequestException("Error getting permissions", "/Users/permissions", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting permissions message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing permissions", ex);
        }
    }

    public List<Permission> GetUserPermissions(int userId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Users/{userId}/Permissions");
        try
        {
            var response = client.Get<List<Permission>>(request);

            if (response == null)
            {
                _logger.Error("Error getting user permissions");
                throw new InvalidHttpRequestException("Error getting permissions", $"/Users/{userId}/permissions", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting user permissions message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing permissions", ex);
        }
    }

    public void SaveUserPermissions(int userId, List<Permission> permissions)
    {
        throw new NotImplementedException();
    }
    
    private void NotifyUserAdded(UserAddedEventArgs ua)
    {
        EventHandler<UserAddedEventArgs> handler = UserAdded;
        handler(this, ua);
    }
    public event EventHandler<UserAddedEventArgs> UserAdded;
    
}