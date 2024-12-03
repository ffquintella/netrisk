using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Entities;
using Model.Exceptions;
using RestSharp;
using Tools.Helpers;
using Exception = System.Exception;

namespace ClientServices.Services;

public class EntitiesRestService(
    IRestService restService,
    IAuthenticationService authenticationService,
    IMemoryCacheService memoryCacheService)
    : RestServiceBase(restService), IEntitiesService
{
    private IMemoryCacheService _memoryCacheService = memoryCacheService;
    
    /*private List<Entity> _cachedEntities = new ();
    private bool _fullCache = false;*/

    public void ClearCache()
    {
        _memoryCacheService.Remove<List<Entity>>("*");
        /*
        _cachedEntities.Clear();
        _fullCache = false;
        */
    }
    
    public EntitiesConfiguration GetEntitiesConfiguration()
    {
        return GetEntitiesConfigurationAsync().GetAwaiter().GetResult();
    }
    
    public async Task<EntitiesConfiguration> GetEntitiesConfigurationAsync()
    {
        var cached = _memoryCacheService.Get<EntitiesConfiguration>("EntitiesConfiguration");
        
        if(cached != null) return cached;
        
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
            
            _memoryCacheService.Set("EntitiesConfiguration", response);
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting entities configuration message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting my entities configuration", ex);
        }
    }

    public async Task<List<Entity>> GetAllAsync(string? definitionName = null, bool loadProperties = true)
    {

        if (definitionName != null)
        {
            if (_memoryCacheService.HasCache<List<Entity>>(definitionName))
            {
                return GetCachedEntities(definitionName);
            }
        }
        else
        {
            if (_memoryCacheService.HasCache<List<Entity>>("All"))
            {
                return GetCachedEntities(definitionName);
            }
        }

        // Else load the cache
        
        var client = RestService.GetClient();
        
        var request = new RestRequest("/Entities");

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
            var response = await client.GetAsync<List<Entity>>(request);

            if (response == null)
            {
                Logger.Error("Error getting entities ");
                throw new RestComunicationException("Error getting entities");
            }
            
            if(definitionName == null)
                _memoryCacheService.Set<List<Entity>>("All", response);
            else 
                _memoryCacheService.Set<List<Entity>>(definitionName, response);

            if (definitionName == null) return GetCachedEntities("All");
            return GetCachedEntities(definitionName);
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting entities message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting entities", ex);
        }
    }
    
    private List<DAL.Entities.Entity> GetCachedEntities(string? definitionName = null)
    {
        List<DAL.Entities.Entity>? result;
        
        if (definitionName != null) result =  _memoryCacheService.Get<List<Entity>>(definitionName);
        else result = _memoryCacheService.Get<List<Entity>>("All");

        if (result == null) throw new Exception("Result cannot be null here");
        
        //result.Sort((e1, e2) => e1.EntitiesProperties.FirstOrDefault(p => p.Type == "name")?.Value.CompareTo(e2.EntitiesProperties.FirstOrDefault(p => p.Type == "name")?.Value) ?? 0);
        
        result = result.OrderBy(e =>  e.EntitiesProperties.FirstOrDefault(p => p.Type == "name")?.Value).ToList();
        
        return result;
    }

    public List<Entity> GetAll(string? definitionName = null, bool loadProperties = true)
    {
        //return GetAllAsync(definitionName).GetAwaiter().GetResult();
        
        return AsyncHelper.RunSync<List<Entity>>(()=> GetAllAsync(definitionName, loadProperties));
    }

    public Entity GetEntity(int entityId, bool loadProperties = true)
    {

        var entity = GetCachedEntities("All").FirstOrDefault(e => e.Id == entityId);
        if (entity != null) return entity;
        
        
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

            var cached = GetCachedEntities("All");
            cached.Add(response);
            _memoryCacheService.Set<List<Entity>>("All", cached);
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
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
            
            //_cachedEntities.Add(response);
            
            var cached = GetCachedEntities("All");
            cached.Add(response);
            _memoryCacheService.Set<List<Entity>>("All", cached);
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating entities message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating entities", ex);
        }
    }

    public Entity? SaveEntity(EntityDto entityDto)
    {
        return SaveEntityAsync(entityDto).GetAwaiter().GetResult();
    }

    public async Task<Entity?> SaveEntityAsync(EntityDto entityDto)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Entities/{entityDto.Id}");

        request.AddJsonBody(entityDto);
        
        try
        {
            var response = await client.PutAsync<Entity>(request);

            if (response == null)
            {
                Logger.Error("Error updating entity {Name}", entityDto.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value);
                throw new RestComunicationException("Error updating entities");
            }
            
            /*
            _cachedEntities.RemoveAll(e => e.Id == entityDto.Id);
            _cachedEntities.Add(response);
            */
            
            var cached = GetCachedEntities("All");
            cached.RemoveAll( e => e.Id == entityDto.Id);
            _memoryCacheService.Set<List<Entity>>("All", cached);
            
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
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
            
            //_cachedEntities.RemoveAll(e => e.Id == entityId);
            
            var cached = GetCachedEntities("All");
            cached.RemoveAll( e => e.Id == entityId);
            _memoryCacheService.Set<List<Entity>>("All", cached);
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating updating message: {Message}", ex.Message);
            throw new RestComunicationException("Error updating entities", ex);
        }
    }
}