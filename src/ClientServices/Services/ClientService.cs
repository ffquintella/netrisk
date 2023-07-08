
using System.Net;
using ClientServices.Interfaces;
using Model;
using Model.Exceptions;
using RestSharp;
using Serilog;
using ILogger = Serilog.ILogger;


namespace ClientServices.Services;

public class ClientService: IClientService
{
    
    private ILogger _logger;
    private IRestService _restService;
    public ClientService(
        IRestService restService)
    {
        _logger = Log.Logger;
        _restService = restService;
    }

    public List<Client> GetAll()
    {
        var restClient = _restService.GetClient();
        var request = new RestRequest("/Clients");
        
        try
        {
            var response = restClient.Get<List<Client>>(request);
            
            return response!;

        }
        catch (Exception ex)
        {
            _logger.Error("Error listing clients {ExMessage}", ex.Message);
            throw new RestComunicationException(ex.Message, ex);
        }
    }

    public int Approve(int id)
    {
        var restClient = _restService.GetClient();
        var request = new RestRequest($"/Clients/{id}/approve");
        
        try
        {
            var response = restClient.Get(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                return 0;
            }

            return -1;

        }
        catch (Exception ex)
        {
            _logger.Error("Error approving client {ExMessage}", ex.Message);
            throw new RestComunicationException(ex.Message, ex);
        }
    }
    public int Reject(int id)
    {
        var restClient = _restService.GetClient();
        var request = new RestRequest($"/Clients/{id}/reject");
        
        try
        {
            var response = restClient.Get(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                return 0;
            }

            return -1;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.Error("Tring to reject already rejected client");
                return -1;
            }
                
            _logger.Error("Error rejecting client {ExMessage}", ex.Message);
            throw new RestComunicationException(ex.Message, ex);
        }
    }
    
    public int Delete(int id)
    {
        var restClient = _restService.GetClient();
        var request = new RestRequest($"/Clients/{id}");
        
        try
        {
            var response = restClient.Delete(request);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                return 0;
            }

            return -1;

        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Error("Tring to delete unexistent client");
                return -1;
            }
            
           _logger.Error("Error deleting client {ExMessage}", ex.Message);
            throw new RestComunicationException(ex.Message, ex);
        }
    }
}