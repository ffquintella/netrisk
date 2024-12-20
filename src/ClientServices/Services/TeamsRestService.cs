using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;
using Tools.Helpers;

namespace ClientServices.Services;

public class TeamsRestService: RestServiceBase, ITeamsService
{

    private IMemoryCacheService MemoryCacheService { get; } = GetService<IMemoryCacheService>();
    
    public TeamsRestService(IRestService restService): base(restService)
    {
    }
    
    public List<Team> GetAll()
    {
        return AsyncHelper.RunSync(GetAllAsync);
    }

    public async Task<List<Team>> GetAllAsync()
    {
        if(MemoryCacheService.HasCache<List<Team>>("all")) return MemoryCacheService.Get<List<Team>>("all")!;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Teams");
        
        try
        {
            var response = await client.GetAsync<List<Team>>(request);

            if (response == null) 
            {
                Logger.Error("Error getting teams");
                throw new RestComunicationException("Error getting teams");
            }
            
            var cachedTeams = response.OrderBy(t => t.Name).ToList();

            MemoryCacheService.Set( "all", cachedTeams, new TimeSpan(0, 60, 0));
            
            return cachedTeams;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Error("Unauthorized teams request");
            }
            Logger.Error("Error getting all teams message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all teams", ex);
        }
    }

    public List<Team> GetByMitigationId(int id)
    {
        throw new NotImplementedException();
    }

    public Team GetById(int teamId, bool fullGet = false)
    {
        return AsyncHelper.RunSync(() => GetByIdAsync(teamId, fullGet));
    }

    public async Task<Team> GetByIdAsync(int teamId, bool fullGet = false)
    {
        
        if(MemoryCacheService.HasCache<Team>(teamId.ToString())) return MemoryCacheService.Get<Team>(teamId.ToString())!;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}");
        
        try
        {
            var response = await client.GetAsync<Team>(request);

            if (response == null) 
            {
                Logger.Error("Error getting team: {Id}", teamId);
                throw new RestComunicationException("Error getting team");
            }
            
            MemoryCacheService.Set(teamId.ToString(), response, new TimeSpan(0, 60, 0));
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Error("Unauthorized team request");
            }
            Logger.Error("Error getting team message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting team", ex);
        }
    }

    public List<int> GetUsersIds(int teamId)
    {
        
        if(MemoryCacheService.HasCache<List<int>>($"team-user-ids-{teamId}") ) return MemoryCacheService.Get<List<int>>($"team-user-ids-{teamId}")!;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}/UserIds");
        
        try
        {
            var response = client.Get<List<int>>(request);

            if (response == null) 
            {
                Logger.Error("Error getting team: {Id} users", teamId);
                throw new RestComunicationException("Error getting team users");
            }
            
            MemoryCacheService.Set($"team-user-ids-{teamId}", response, new TimeSpan(0, 60, 0));
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Error("Unauthorized team request");
            }
            Logger.Error("Error getting team users message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting team users", ex);
        }
    }

    public void UpdateUsers(int teamId, List<int> usersIds)
    {
        
        MemoryCacheService.Remove<List<int>>($"team-user-ids-{teamId}");
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}/UserIds");

        request.AddJsonBody(usersIds);
        
        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK) 
            {
                Logger.Error("Error updating team: {Id} users", teamId);
                throw new RestComunicationException("Error updating team users");
            }
     
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Error("Unauthorized team update request");
            }
            Logger.Error("Error updating team users message: {Message}", ex.Message);
            throw new RestComunicationException("Error updating team users", ex);
        }
    }

    public void Delete(int teamId)
    {
        
        MemoryCacheService.Remove<Team>(teamId.ToString());
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}");
        
        try
        {
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK) 
            {
                Logger.Error("Error deleting team: {Id}", teamId);
                throw new RestComunicationException("Error deleting team");
            }
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Error("Unauthorized team delete request");
            }
            Logger.Error("Error deleting team message: {Message}", ex.Message);
            throw new RestComunicationException("Error deleting team", ex);
        }
    }

    public Team Create(Team team)
    {
        using var client = RestService.GetClient();

        team.Value = 0;
        
        var request = new RestRequest($"/Teams");

        request.AddJsonBody(team);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created) 
            {
                Logger.Error("Error creating team");
                throw new RestComunicationException("Error creating team");
            }
     
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var newTeam = JsonSerializer.Deserialize<Team>(response.Content!, options)!;
            
            MemoryCacheService.Set(team.Value.ToString(), team, new TimeSpan(0, 60, 0));
            
            return newTeam;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.Error("Unauthorized team create request");
            }
            Logger.Error("Error creating team message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating team", ex);
        }
    }
}