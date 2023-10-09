using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class HostsRestService: RestServiceBase, IHostsService
{
    public HostsRestService(IRestService restService) : base(restService)
    {
    }

    public Host? GetOne(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{id}");
        
        
        try
        {
            var response = client.Get<Host>(request);

            if (response != null) return response;
            Logger.Error("Error getting host");
            throw new InvalidHttpRequestException("Error getting host", $"/Hosts/{id}", "GET");

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting host message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting host", ex);
        } 
    }

    public List<Host> GetAll()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts");
        try
        {
            var response = client.Get<List<Host>>(request);

            if (response == null)
            {
                Logger.Error("Error listing hosts");
                throw new InvalidHttpRequestException("Error listing hosts", "/Hosts", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing hosts message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing hosts", ex);
        }
    }

    public Host? Create(Host host)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts");
        
        try
        {
            request.AddJsonBody(host);
            
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error creating host");
                throw new InvalidHttpRequestException("Error creating host", "/Hosts", "POST");
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var newHost = JsonSerializer.Deserialize<Host?>(response.Content!, options);
            
            return newHost;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating host message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating host", ex);
        }
    }
}