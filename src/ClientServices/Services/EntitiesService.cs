using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class EntitiesService: ServiceBase, IEntitiesService
{
    private IAuthenticationService _authenticationService;
    
    public EntitiesService(IRestService restService, IAuthenticationService authenticationService) : base(restService)
    {
        _authenticationService = authenticationService;
    }

    public async Task<EntitiesConfiguration> GetEntitiesConfigurationAsync()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Entities/Configuration");
        
        try
        {
            var response = await client.GetAsync<EntitiesConfiguration>(request);

            if (response == null)
            {
                _logger.Error("Error getting entities configuration ");
                throw new RestComunicationException("Error getting entities configuration");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting entities configuration message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting my entities configuration", ex);
        }
    }

    public async Task<List<Entity>> GetAllAsync()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Entities");

        request.AddParameter("propertyLoad", "true");
        
        try
        {
            var response = await client.GetAsync<List<Entity>>(request);

            if (response == null)
            {
                _logger.Error("Error getting entities ");
                throw new RestComunicationException("Error getting entities");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error getting entities message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting entities", ex);
        }
    }
}