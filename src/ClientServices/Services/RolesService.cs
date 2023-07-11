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
}
    
