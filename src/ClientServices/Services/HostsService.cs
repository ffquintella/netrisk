using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class HostsService: ServiceBase, IHostsService
{
    public HostsService(IRestService restService) : base(restService)
    {
    }

    public Host? GetOne(int id)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Hosts/{id}");
        
        
        try
        {
            var response = client.Get<Host>(request);

            if (response != null) return response;
            _logger.Error("Error getting host");
            throw new InvalidHttpRequestException("Error getting host", $"/Hosts/{id}", "GET");

        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error getting host message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting host", ex);
        } 
    }

    public List<Host> GetAll()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Hosts");
        try
        {
            var response = client.Get<List<Host>>(request);

            if (response == null)
            {
                _logger.Error("Error listing hosts");
                throw new InvalidHttpRequestException("Error listing hosts", "/Hosts", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error listing hosts message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing hosts", ex);
        }
    }
}