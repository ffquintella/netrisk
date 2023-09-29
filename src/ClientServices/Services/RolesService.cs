using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class RolesService : ServiceBase, IRolesService
{

    public RolesService(IRestService restService) : base(restService)
    {

    }

    public List<Role> GetAllRoles()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Roles");

        try
        {
            var response = client.Get<List<Role>>(request);

            if (response == null)
            {
                _logger.Error("Error getting roles");
                response = new List<Role>();
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting all roles message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all roles", ex);
        }
    }

    public void Delete(int roleId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Roles/{roleId}");

        try
        {
            var response = client.Delete(request);

            if (response.StatusCode  != HttpStatusCode.OK)
            {
                _logger.Error("Error deleting role");
                throw new RestComunicationException("Error deleting role");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error deleting role message: {Message}", ex.Message);
            throw new RestComunicationException("Error deleting role", ex);
        }
    }

    public Role Create(Role role)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Roles");

        request.AddJsonBody(role);
        
        try
        {
            var response = client.Post<Role>(request);

            if (response == null)
            {
                _logger.Error("Error creating role");
                throw new RestComunicationException("Error creating role");
            }
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error creating role message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating role", ex);
        }
    }

    public List<string> GetRolePermissions(int roleId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Roles/{roleId}/Permissions");

        try
        {
            var response = client.Get<List<string>>(request);

            if (response == null)
            {
                _logger.Error("Error getting role permissions");
                throw new DataNotFoundException("netrisk", "role", new Exception("Role not found"));
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting role permissions message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting role permissions", ex);
        }
    }
}
    
