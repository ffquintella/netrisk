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

    public List<Entity> GetAll(string? definitionName = null)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Entities");

        request.AddParameter("propertyLoad", "true");

        if (definitionName != null)
        {
            request.AddParameter("entityDefinition", definitionName);
        }
        
        try
        {
            var response = client.Get<List<Entity>>(request);

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

    public Entity? CreateEntity(EntityDto entityDto)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Entities");

        request.AddJsonBody(entityDto);

        try
        {
            var response = client.Post<Entity>(request);

            if (response == null)
            {
                _logger.Error("Error creating entity {Name}", entityDto.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value);
                throw new RestComunicationException("Error creating entities");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            _logger.Error("Error creating entities message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating entities", ex);
        }
    }
}