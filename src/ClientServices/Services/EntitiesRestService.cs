using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class EntitiesRestService: RestServiceBase, IEntitiesService
{
    private IAuthenticationService _authenticationService;
    
    private EntitiesConfiguration? _entitiesConfiguration;
    
    public EntitiesRestService(IRestService restService, IAuthenticationService authenticationService) : base(restService)
    {
        _authenticationService = authenticationService;
    }

    public EntitiesConfiguration GetEntitiesConfiguration()
    {
        if(_entitiesConfiguration != null) return _entitiesConfiguration;
        
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Entities/Configuration");
        
        try
        {
            var response = client.Get<EntitiesConfiguration>(request);

            if (response == null)
            {
                Logger.Error("Error getting entities configuration ");
                throw new RestComunicationException("Error getting entities configuration");
            }
            
            _entitiesConfiguration = response;
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting entities configuration message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting my entities configuration", ex);
        }
    }
    
    public async Task<EntitiesConfiguration> GetEntitiesConfigurationAsync()
    {
        if(_entitiesConfiguration != null) return _entitiesConfiguration;
        
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Entities/Configuration");
        
        try
        {
            var response = await client.GetAsync<EntitiesConfiguration>(request);

            if (response == null)
            {
                Logger.Error("Error getting entities configuration ");
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
            Logger.Error("Error getting entities configuration message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting my entities configuration", ex);
        }
    }
    
    

    public List<Entity> GetAll(string? definitionName = null, bool loadProperties = true)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Entities");

        //request.AddParameter("propertyLoad", "true");

        if (definitionName != null)
        {
            request.AddParameter("entityDefinition", definitionName);
        }
        if (loadProperties == true)
        {
            request.AddParameter("propertyLoad", loadProperties);
        }
        
        try
        {
            var response = client.Get<List<Entity>>(request);

            if (response == null)
            {
                Logger.Error("Error getting entities ");
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
            Logger.Error("Error getting entities message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting entities", ex);
        }
    }

    public Entity GetEntity(int entityId, bool loadProperties = true)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Entities/{entityId}");

        if (loadProperties == true)
        {
            request.AddParameter("propertyLoad", loadProperties);
        }
        
        try
        {
            var response = client.Get<Entity>(request);

            if (response == null)
            {
                Logger.Error("Error getting entity {Id}", entityId);
                throw new RestComunicationException($"Error getting entity {entityId}" );
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting entity message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting entity", ex);
        }
    }
    
    public async Task<List<EntitiesPropertyDto>> GetMandatoryPropertiesAsync(string definitionName)
    {
        var configuration = await GetEntitiesConfigurationAsync();
        
        var result = new List<EntitiesPropertyDto>();

        var properties = configuration.Definitions[definitionName].Properties.Where(p => p.Value.Nullable == false)
            .ToList();
        
        foreach (var propertyKV in properties)
        {
           
            var property = new EntitiesPropertyDto()
            {
                Type = propertyKV.Key,
                Value = propertyKV.Value.DefaultValue!,
                Name = propertyKV.Key + "-"
            };
            result.Add(property);
        }

        return result;
    }

    public Entity? CreateEntity(EntityDto entityDto)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Entities");

        request.AddJsonBody(entityDto);

        try
        {
            var response = client.Post<Entity>(request);

            if (response == null)
            {
                Logger.Error("Error creating entity {Name}", entityDto.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value);
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
            Logger.Error("Error creating entities message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating entities", ex);
        }
    }

    public Entity? SaveEntity(EntityDto entityDto)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Entities/{entityDto.Id}");

        request.AddJsonBody(entityDto);
        
        try
        {
            var response = client.Put<Entity>(request);

            if (response == null)
            {
                Logger.Error("Error updating entity {Name}", entityDto.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value);
                throw new RestComunicationException("Error updating entities");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating updating message: {Message}", ex.Message);
            throw new RestComunicationException("Error updating entities", ex);
        }
    }

    public void Delete(int entityId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Entities/{entityId}");

      
        try
        {
            var response = client.Delete(request);

            if (response == null)
            {
                Logger.Error("Error deleting entity {Id}", entityId);
                throw new RestComunicationException("Error deleting entities");
            }
            
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating updating message: {Message}", ex.Message);
            throw new RestComunicationException("Error updating entities", ex);
        }
    }
}