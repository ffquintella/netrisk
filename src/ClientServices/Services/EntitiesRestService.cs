using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
    
    public async Task<List<Entity>> GetAllFromServerAsync(bool loadProperties = true)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Entities");
        if (loadProperties) request.AddParameter("propertyLoad", loadProperties);

        try
        {
            var response = await client.GetAsync<List<Entity>>(request);
            if (response == null)
            {
                Logger.Error("Error getting entities from the server");
                throw new RestComunicationException("Error getting entities");
            }

            return response.OrderBy(e => e.EntitiesProperties?.FirstOrDefault(p => p.Type == "name")?.Value).ToList();
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized) authenticationService.DiscardAuthenticationToken();
            Logger.Error("Error getting entities from the server message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting entities", ex);
        }
    }

    private List<DAL.Entities.Entity> GetCachedEntities(string? definitionName = null)
    {
        List<DAL.Entities.Entity>? result;
        
        if (definitionName != null) result =  _memoryCacheService.Get<List<Entity>>(definitionName);
        else result = _memoryCacheService.Get<List<Entity>>("All");

        if (result == null) throw new NullObjectException($"entities cache {definitionName ?? "All"}");
        
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

    // --- risk context (S40 §6.7, §8.5; T264) ------------------------------------------------------------------------
    // Through the error-reporting client, never the reliable one: a refusal carries the stable code the screen translates,
    // and the batch is a write that must not be retried.

    /// <summary>The API's MVC defaults: camelCase names, case-insensitive reads.</summary>
    private static readonly JsonSerializerOptions RiskContextJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<EntityRiskContextRowDto>> GetRiskContextAsync(IEnumerable<string>? types = null,
        CancellationToken ct = default)
    {
        var request = new RestRequest("/Entities/RiskContext") { Method = Method.Get };
        var list = types?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        if (list is { Count: > 0 }) request.AddQueryParameter("types", string.Join(",", list));

        return ReadRiskContext<List<EntityRiskContextRowDto>>(await ExecuteRiskContextAsync(request, ct));
    }

    public async Task<EntityRiskContextBatchResultDto> SaveRiskContextAsync(EntityRiskContextBatchRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var put = new RestRequest("/Entities/RiskContext") { Method = Method.Put };
        put.AddStringBody(JsonSerializer.Serialize(request, RiskContextJson), ContentType.Json);

        var result = ReadRiskContext<EntityRiskContextBatchResultDto>(await ExecuteRiskContextAsync(put, ct));

        // The cached entities still hold the values the batch just replaced.
        if (result.UpdatedEntities > 0) ClearCache();
        return result;
    }

    public async Task<IReadOnlyList<EntityHistoryEntryDto>> GetHistoryAsync(int entityId, int limit = 200,
        CancellationToken ct = default)
    {
        var request = new RestRequest($"/Entities/{entityId.ToString(CultureInfo.InvariantCulture)}/History")
            { Method = Method.Get };
        request.AddQueryParameter("limit", limit.ToString(CultureInfo.InvariantCulture));

        return ReadRiskContext<List<EntityHistoryEntryDto>>(await ExecuteRiskContextAsync(request, ct));
    }

    /// <summary>One exchange; a transport failure is a communication error, a non-2xx a <see cref="InvalidHttpRequestException"/>.</summary>
    private async Task<RestResponse> ExecuteRiskContextAsync(RestRequest request, CancellationToken ct)
    {
        var route = request.Resource;
        var method = request.Method.ToString().ToUpperInvariant();
        using var client = RestService.GetClient(reportErrorResponses: true);

        RestResponse response;
        try
        {
            response = await client.ExecuteAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Method} {Route}: {Message}", method, route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }

        if (response.StatusCode == 0)
        {
            Logger.Error("{Method} {Route} did not reach the server: {Message}", method, route, response.ErrorMessage);
            throw new RestComunicationException($"Error calling {route}",
                response.ErrorException ?? new HttpRequestException(response.ErrorMessage));
        }

        if (response.IsSuccessStatusCode) return response;

        // 401: the session is gone; the next call must sign in again rather than resend a dead token.
        if (response.StatusCode == HttpStatusCode.Unauthorized) authenticationService.DiscardAuthenticationToken();

        var (code, message) = ReadRefusal(response.Content);
        Logger.Warning("{Method} {Route} refused with {Status} {Code}", method, route, (int)response.StatusCode, code ?? "-");
        throw new InvalidHttpRequestException(string.IsNullOrWhiteSpace(message) ? $"Error calling {route}" : message,
            route, method) { StatusCode = response.StatusCode, ErrorCode = code };
    }

    private static T ReadRiskContext<T>(RestResponse response) where T : class
    {
        var route = response.Request?.Resource ?? "";
        var method = response.Request?.Method.ToString().ToUpperInvariant() ?? "GET";
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidHttpRequestException("The server answered with an empty body.", route, method)
                { StatusCode = response.StatusCode };

        try
        {
            return JsonSerializer.Deserialize<T>(response.Content, RiskContextJson)
                   ?? throw new InvalidHttpRequestException("The server answered with an unreadable body.", route, method)
                       { StatusCode = response.StatusCode };
        }
        catch (JsonException ex)
        {
            throw new InvalidHttpRequestException($"The server answered {route} with an unreadable body: {ex.Message}",
                route, method) { StatusCode = response.StatusCode };
        }
    }

    /// <summary>The <c>{ error, message }</c> of a refusal; both null when the body is empty or not that shape.</summary>
    private static (string? Code, string? Message) ReadRefusal(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            return (Text(document.RootElement, "error"), Text(document.RootElement, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }

        static string? Text(JsonElement root, string name)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.String &&
                       !string.IsNullOrWhiteSpace(property.Value.GetString())
                    ? property.Value.GetString()
                    : null;
            }

            return null;
        }
    }
}