using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
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

    public bool HostExists(string hostIp)
    {
        var client = RestService.GetClient();
        
        if(hostIp == null) throw new ArgumentNullException(nameof(hostIp));
        
        var request = new RestRequest($"/Hosts/Find");
        request.AddParameter("ip", hostIp);
        
        try
        {
            
            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
                
            } else if (response.StatusCode == HttpStatusCode.OK)
            {
                return true;
            }
            else
            {
                Logger.Error("Error finding host");
                throw new InvalidHttpRequestException("Error finding host", "/Hosts/Find", "GET");
            }   
            

            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error finding host message:{Message}", ex.Message);
            throw new RestComunicationException("Error finding host", ex);
        }
    }

    public Host? GetByIp(string hostIp)
    {
        var client = RestService.GetClient();
        
        if(hostIp == null) throw new ArgumentNullException(nameof(hostIp));
        
        var request = new RestRequest($"/Hosts/Find");
        request.AddParameter("ip", hostIp);
        
        try
        {
            
            var response = client.Get<Host>(request);

            if (response != null)
            {
                return response;
            } 
            Logger.Error("Error finding host");
            throw new InvalidHttpRequestException("Error finding host", "/Hosts/Find", "GET");
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error finding host message:{Message}", ex.Message);
            throw new RestComunicationException("Error finding host", ex);
        }
    }

    public void Update(Host host)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{host.Id}");
        
        try
        {
            request.AddJsonBody(host);
            
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating host");
                throw new InvalidHttpRequestException("Error updating host", $"/Hosts/{host.Id}", "PUT");
            }
          
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating host message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating host", ex);
        }
    }

    public HostsService GetHostService(int hostId, int serviceId)
    {

        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/{serviceId}");
        
        try
        {
            var response = client.Get<HostsService>(request);            
            

            if (response == null )
            {
                Logger.Error("Error getting host service");
                throw new InvalidHttpRequestException("Error getting host service", $"/Hosts/{hostId}/Services/{serviceId}", "GET");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting host service message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting host service", ex);
        }
    }

    public bool HostHasService(int hostId, string name, int? port, string protocol)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/Exists");
        request.AddParameter("name", name);
        if(port != null) request.AddParameter("port", port.Value);
        request.AddParameter("protocol", protocol);
        
        try
        {
            var response = client.Get<bool>(request);            
            
            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error verifying service message:{Message}", ex.Message);
            throw new RestComunicationException("Error verifying host service", ex);
        }
    }

    public HostsService CreateAndAddService(int hostId, HostsServiceDto service)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services");
        request.AddJsonBody(service);
        
        try
        {
            var response = client.Post<HostsService>(request);            
            

            if (response == null )
            {
                Logger.Error("Error creating host service");
                throw new InvalidHttpRequestException("Error creating host service", $"/Hosts/{hostId}/Services", "POST");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting creating service message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating host service", ex);
        }
    }

    public void DeleteService(int hostId, int serviceId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/{serviceId}");

        
        try
        {
            var response = client.Delete(request);            
            

            if (response.StatusCode == HttpStatusCode.OK )
            {
                Logger.Error("Error deleting host service");
                throw new InvalidHttpRequestException("Error deleting host service", $"/Hosts/{hostId}/Services/{serviceId}", "DELETE");
            }


        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting deleting service message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting host service", ex);
        }
    }

    public void UpdateService(int hostId, HostsServiceDto service)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/{service.Id}");
        request.AddJsonBody(service);
        
        try
        {
            var response = client.Put(request);            
            

            if (response.StatusCode != HttpStatusCode.OK )
            {
                Logger.Error("Error updating host service");
                throw new InvalidHttpRequestException("Error updating host service", $"/Hosts/{hostId}/Services", "POST");
            }

            

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting updating service message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating host service", ex);
        }
    }
}