using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Tools.Helpers;

namespace ClientServices.Services;

public class MitigationRestService: RestServiceBase, IMitigationService
{

    private IAuthenticationService _authenticationService;
    
    public MitigationRestService(IRestService restService, 
        IAuthenticationService authenticationService): base(restService)
    {
        _authenticationService = authenticationService;
    }

    public List<Team>? GetTeamsById(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/{id}/Teams");
        try
        {
            var response = client.Get<List<Team>>(request);
            if (response == null)
            {
                Logger.Error("Error getting teams for mitigation {Id}", id);
                throw new RestComunicationException($"Error getting teams for mitigation {id}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting teams for mitigation  message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting teams for mitigation", ex);
        }
    }
    
    public Mitigation? GetByRiskId(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{id}/Mitigation");
        try
        {
            var response = client.Get(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Warning("Error getting mitigation for risk {Id}", id);
                return null;
            }
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return response.Content == null ? null : JsonSerializer.Deserialize<Mitigation>(response.Content, options);
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting mitigation  message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk mitigation", ex);
        }
    }

    public async Task<Mitigation?> GetByRiskIdAsync(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Risks/{id}/Mitigation");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Warning("Error getting mitigation for risk {Id}", id);
                return null;
            }
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            //
            //return response.Content == null ? null : JsonSerializer.Deserialize<Mitigation>(response.Content, options);
            if (response.Content == null)
            {
                return null;
            }
            else
            {
                using var stream = new MemoryStream();
                StreamWriter writer = new StreamWriter(stream);
                writer.Write(response.Content);
                writer.Flush();
                stream.Position = 0;
                
                return await JsonSerializer.DeserializeAsync<Mitigation>(stream, options);
            }
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Warning("Error getting mitigation  message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting risk mitigation", ex);
        }
    }
    
    public List<FileListing> GetFiles(int mitigationId)
    {
        return AsyncHelper.RunSync(() => GetFilesAsync(mitigationId));
    }

    public async Task<List<FileListing>> GetFilesAsync(int mitigationId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/{mitigationId}/Files");
        try
        {
            var response = await client.GetAsync<List<FileListing>>(request);

            if (response == null)
            {
                Logger.Error("Error getting files for mitigation: {Id}", mitigationId);
                throw new RestComunicationException($"Error getting files for mitigation: {mitigationId}");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting mitigation files message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting mitigation files", ex);
        }
    }

    public List<PlanningStrategy>? GetStrategies()
    {
        return AsyncHelper.RunSync(GetStrategiesAsync);
    }

    public async Task<List<PlanningStrategy>?> GetStrategiesAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/Strategies");
        try
        {
            var response = await client.GetAsync<List<PlanningStrategy>>(request);

            if (response == null)
            {
                Logger.Error("Error getting mitigation strategies");
                throw new RestComunicationException($"Error getting mitigation strategies");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting mitigation strategies");
            throw new RestComunicationException("Error getting mitigation strategies", ex);
        }
    }

    public List<MitigationCost>? GetCosts()
    {
        return AsyncHelper.RunSync(GetCostsAsync);
    }

    public async Task<List<MitigationCost>?> GetCostsAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/Costs");
        try
        {
            var response = await client.GetAsync<List<MitigationCost>>(request);

            if (response == null)
            {
                Logger.Error("Error getting mitigation costs");
                throw new RestComunicationException($"Error getting mitigation costs");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting mitigation costs");
            throw new RestComunicationException("Error getting mitigation costs", ex);
        } 
    }

    public List<MitigationEffort>? GetEfforts()
    {
       return AsyncHelper.RunSync(GetEffortsAsync);
    }

    public async Task<List<MitigationEffort>?> GetEffortsAsync()
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/Efforts");
        try
        {
            var response = await client.GetAsync<List<MitigationEffort>>(request);

            if (response == null)
            {
                Logger.Error("Error getting mitigation efforts");
                throw new RestComunicationException($"Error getting mitigation efforts");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting mitigation efforts");
            throw new RestComunicationException("Error getting mitigation efforts", ex);
        } 
    }
    
    public Mitigation? GetById(int id)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/{id}");
        try
        {
            var response = client.Get<Mitigation>(request);

            if (response == null)
            {
                Logger.Error("Error getting mitigation");
                throw new RestComunicationException($"Error getting mitigation");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting mitigation");
            throw new RestComunicationException("Error getting mitigation", ex);
        } 
    }

    public void Save(MitigationDto mitigation)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/{mitigation.Id}");

        request.AddJsonBody(mitigation);
        
        try
        {
            var response = client.Put(request);

            if (response == null)
            {
                Logger.Error("Error saving mitigation");
                throw new RestComunicationException($"Error saving mitigation");
            }
            

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error saving mitigation");
            throw new RestComunicationException("Error saving mitigation", ex);
        } 
    }
    public Mitigation? Create(MitigationDto mitigation)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations");

        request.AddJsonBody(mitigation);
        
        try
        {
            var response = client.Post<Mitigation>(request);

            if (response == null)
            {
                Logger.Error("Error creating mitigation");
                throw new RestComunicationException($"Error creating mitigation");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error creating mitigation");
            throw new RestComunicationException("Error creating mitigation", ex);
        } 
    }

    public void DeleteTeamsAssociations(int mitigationId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/{mitigationId}/Teams");

        try
        {
            var response = client.Delete(request);

            if (response == null)
            {
                Logger.Error("Error deleting mitigation associations");
                throw new RestComunicationException($"Error deleting mitigation associations");
            }
            
            if(response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting mitigation associations");
                throw new RestComunicationException($"Error deleting mitigation associations");
            }
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error deleting mitigation associations");
            throw new RestComunicationException("Error deleting mitigation associations", ex);
        } 
    }

    public void AssociateMitigationToTeam(int mitigationId, int teamId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Mitigations/{mitigationId}/Teams/Associate/{teamId}");

        try
        {
            var response = client.Get(request);

            if (response == null)
            {
                Logger.Error("Error associating mitigation to team");
                throw new RestComunicationException($"Error associating mitigation to team");
            }
            
            if(response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error associating mitigation to team");
                throw new RestComunicationException($"Error associating mitigation to team");
            }
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error associating mitigation to team");
            throw new RestComunicationException("Error associating mitigation to team", ex);
        } 
    }
    
}