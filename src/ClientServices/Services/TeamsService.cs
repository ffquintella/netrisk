using System.Net;
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
    
}