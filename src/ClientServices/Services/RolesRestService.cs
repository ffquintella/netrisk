using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class RolesRestService : RestServiceBase, IRolesService
{

    public RolesRestService(IRestService restService) : base(restService)
    {

    }

    public List<Role> GetAllRoles()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Roles");

        try
        {
            var response = client.Get<List<Role>>(request);

            if (response == null)
            {
                Logger.Error("Error getting roles");
                response = new List<Role>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting all roles message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all roles", ex);
        }
    }
    
    public async Task<List<Role>> GetAllRolesAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Roles");

        try
        {
            var response = await client.GetAsync<List<Role>>(request);

            if (response == null)
            {
                Logger.Error("Error getting roles");
                response = new List<Role>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting all roles message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all roles", ex);
        }
    }

    public void Delete(int roleId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Roles/{roleId}");

        try
        {
            var response = client.Delete(request);

            if (response.StatusCode  != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting role");
                throw new RestComunicationException("Error deleting role");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting role message: {Message}", ex.Message);
            throw new RestComunicationException("Error deleting role", ex);
        }
    }

    public Role Create(Role role)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Roles");

        request.AddJsonBody(role);
        
        try
        {
            var response = client.Post<Role>(request);

            if (response == null)
            {
                Logger.Error("Error creating role");
                throw new RestComunicationException("Error creating role");
            }
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating role message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating role", ex);
        }
    }

    public List<string> GetRolePermissions(int roleId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Roles/{roleId}/Permissions");

        try
        {
            var response = client.Get<List<string>>(request);

            if (response == null)
            {
                Logger.Error("Error getting role permissions");
                throw new DataNotFoundException("netrisk", "role", new Exception("Role not found"));
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting role permissions message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting role permissions", ex);
        }
    }

    public void UpdateRolePermissions(int roleId, List<string> permissions)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Roles/{roleId}/Permissions");
        
        request.AddJsonBody(permissions);

        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK )
            {
                Logger.Error("Error updating role permissions");
                throw new Exception("Error updating role permissions");
            }
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating role permissions message: {Message}", ex.Message);
            throw new RestComunicationException("Error updating role permissions", ex);
        }
    }
}
    
