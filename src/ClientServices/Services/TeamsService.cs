using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class TeamsService: ServiceBase, ITeamsService
{
    
    public TeamsService(IRestService restService): base(restService)
    {
    }
    
    public List<Team> GetAll()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest("/Teams");
        
        try
        {
            var response = client.Get<List<Team>>(request);

            if (response == null) 
            {
                _logger.Error("Error getting teams");
                throw new RestComunicationException("Error getting teams");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Error("Unauthorized teams request");
            }
            _logger.Error("Error getting all teams message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all teams", ex);
        }
    }

    public List<Team> GetByMitigationId(int id)
    {
        throw new NotImplementedException();
    }

    public Team GetById(int teamId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}");
        
        try
        {
            var response = client.Get<Team>(request);

            if (response == null) 
            {
                _logger.Error("Error getting team: {Id}", teamId);
                throw new RestComunicationException("Error getting team");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Error("Unauthorized team request");
            }
            _logger.Error("Error getting team message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting team", ex);
        }
    }

    public List<int> GetUsersIds(int teamId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}/UserIds");
        
        try
        {
            var response = client.Get<List<int>>(request);

            if (response == null) 
            {
                _logger.Error("Error getting team: {Id} users", teamId);
                throw new RestComunicationException("Error getting team users");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Error("Unauthorized team request");
            }
            _logger.Error("Error getting team users message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting team users", ex);
        }
    }

    public void UpdateUsers(int teamId, List<int> usersIds)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}/UserIds");

        request.AddJsonBody(usersIds);
        
        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK) 
            {
                _logger.Error("Error updating team: {Id} users", teamId);
                throw new RestComunicationException("Error updating team users");
            }
     
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Error("Unauthorized team update request");
            }
            _logger.Error("Error updating team users message: {Message}", ex.Message);
            throw new RestComunicationException("Error updating team users", ex);
        }
    }

    public void Delete(int teamId)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Teams/{teamId}");
        
        try
        {
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK) 
            {
                _logger.Error("Error deleting team: {Id}", teamId);
                throw new RestComunicationException("Error deleting team");
            }
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Error("Unauthorized team delete request");
            }
            _logger.Error("Error deleting team message: {Message}", ex.Message);
            throw new RestComunicationException("Error deleting team", ex);
        }
    }

    public Team Create(Team team)
    {
        var client = _restService.GetClient();

        team.Value = 0;
        
        var request = new RestRequest($"/Teams");

        request.AddJsonBody(team);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created) 
            {
                _logger.Error("Error creating team");
                throw new RestComunicationException("Error creating team");
            }
     
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var newTeam = JsonSerializer.Deserialize<Team>(response.Content!, options)!;
            
            return newTeam;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Error("Unauthorized team create request");
            }
            _logger.Error("Error creating team message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating team", ex);
        }
    }
}