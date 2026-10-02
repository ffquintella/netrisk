using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Tools.Helpers;

namespace ClientServices.Services;

public class HostsRestService: RestServiceBase, IHostsService
{
    private List<Host> _cachedHosts = new ();
    private bool _fullCache = false;
    
    public HostsRestService(IRestService restService) : base(restService)
    {
    }

    public Host? GetOne(int id)
    {
        if(_fullCache) return _cachedHosts.FirstOrDefault(h => h.Id == id);
        
        var foundHost = _cachedHosts.FirstOrDefault(h => h.Id == id);
        if (foundHost != null) return foundHost;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{id}");
        
        try
        {
            var response = client.Get<Host>(request);

            if (response != null)
            {
                _cachedHosts.Add(response);
                _cachedHosts = _cachedHosts.OrderBy(h => h.HostName).ToList();
                return response;
            }
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
        return AsyncHelper.RunSync(GetAllAsync);
    }

    public async Task<List<Host>> GetAllAsync()
    {
        if (_fullCache) return _cachedHosts;
        
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts");
        try
        {
            var response = await client.GetAsync<List<Host>>(request);

            if (response == null)
            {
                Logger.Error("Error listing hosts");
                throw new InvalidHttpRequestException("Error listing hosts", "/Hosts", "GET");
            }
            
            _cachedHosts = response.OrderBy(h => h.HostName).ToList();
            _fullCache = true;
            
            return _cachedHosts;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing hosts message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing hosts", ex);
        }
    }

    public async Task<List<Host>> GetFilteredAsync(int pageSize, int pageNumber, string? filter)
    {
        var (items, _) = await GetFilteredAsync(pageSize, pageNumber, filter, IHostsService.DefaultSort);
        return items;
    }

    public async Task<(List<Host> Items, int Total)> GetFilteredAsync(int pageSize, int pageNumber,
        string? filter, string? sorts)
    {
        using var client = RestService.GetClient();
        string cultureCode = CultureInfo.CurrentCulture.Name;

        var request = new RestRequest($"/Hosts/Filtered");

        request.AddParameter("page", pageNumber);
        request.AddParameter("pageSize", pageSize);
        request.AddParameter("culture", cultureCode);

        if (filter is { Length: > 0 }) request.AddParameter("filters", filter);
        // Sorted by the server, so the order holds across pages; sorting each page here only
        // ordered the hundred rows that happened to arrive.
        if (sorts is { Length: > 0 }) request.AddParameter("sorts", sorts);

        try
        {
            // GetAsync raises an HttpRequestException carrying the status for anything but a 2xx or
            // a 404, so a rejected filter arrives in the catch below whichever client this is.
            var response = await client.GetAsync(request);

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
                throw new BadFilterException(filter ?? "", response.Content ?? "");

            if (response.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(response.Content))
            {
                Logger.Error("Error listing hosts: {Status}", response.StatusCode);
                throw new InvalidHttpRequestException("Error listing hosts", "/Hosts/Filtered", "GET");
            }

            var hosts = JsonSerializer.Deserialize<List<Host>>(response.Content, JsonOptions)
                        ?? throw new InvalidHttpRequestException("Error listing hosts", "/Hosts/Filtered", "GET");

            return (hosts, ReadTotalCount(response, hosts.Count));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            throw new BadFilterException(filter ?? "", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing hosts message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing hosts", ex);
        }
    }

    /// <summary>
    /// <c>X-Total-Count</c>, or the page's own row count when the header is missing or unreadable —
    /// a lower bound that keeps "1–n of total" true rather than reporting zero hosts beside a full page.
    /// </summary>
    private static int ReadTotalCount(RestResponse response, int fallback)
    {
        var header = response.Headers?.FirstOrDefault(h =>
            string.Equals(h.Name, "X-Total-Count", StringComparison.OrdinalIgnoreCase));

        return int.TryParse(header?.Value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture,
            out var total)
            ? total
            : fallback;
    }

    public Task<List<string>> GetEnvironmentsAsync() =>
        GetJsonAsync<List<string>>("/Hosts/Environments", "host environments");

    public Task<HostVulnerabilitySummaryDto> GetVulnerabilitySummaryAsync(int hostId) =>
        GetJsonAsync<HostVulnerabilitySummaryDto>($"/Hosts/{hostId}/VulnerabilitySummary",
            "host vulnerability summary");

    public Task<List<AuditLog>> GetHistoryAsync(int hostId, int limit = 500) =>
        GetJsonAsync<List<AuditLog>>($"/Hosts/{hostId}/History", "host history",
            ("limit", limit.ToString(CultureInfo.InvariantCulture)));

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// A read that must answer 200 with a body. A 404 is the host being gone or out of the user's
    /// scope, and is reported as <see cref="DataNotFoundException"/>; RestSharp hands a 404 back as
    /// a response rather than raising it, and the exception filter covers a client that does raise.
    /// </summary>
    private async Task<T> GetJsonAsync<T>(string route, string what, params (string Name, string Value)[] query)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);
        foreach (var (name, value) in query) request.AddQueryParameter(name, value);

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException("hosts", route);

            if (response.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(response.Content))
            {
                Logger.Error("Error getting {What}: {Status}", what, response.StatusCode);
                throw new InvalidHttpRequestException($"Error getting {what}", route, "GET");
            }

            return JsonSerializer.Deserialize<T>(response.Content, JsonOptions)
                   ?? throw new InvalidHttpRequestException($"Error getting {what}", route, "GET");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new DataNotFoundException("hosts", route, ex);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting {What} message:{Message}", what, ex.Message);
            throw new RestComunicationException($"Error getting {what}", ex);
        }
    }

    public async Task<Host?> Create(Host host)
    {
        using var client = MutatingClient();
        
        var request = new RestRequest($"/Hosts");
        
        try
        {
            request.AddJsonBody(host);
            
            var response = await client.PostAsync(request);

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
        using var client = RestService.GetClient();
        
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

    public async Task<bool> HostExistsAsync(string hostIp)
    {
        using var client = RestService.GetClient();
        
        if(hostIp == null) throw new ArgumentNullException(nameof(hostIp));
        
        var request = new RestRequest($"/Hosts/Find");
        request.AddParameter("ip", hostIp);
        
        try
        {
            var response = await client.GetAsync(request);

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
        using var client = RestService.GetClient();
        
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

    public async Task<Host> GetByIpAsync(string hostIp)
    {
        using var client = RestService.GetClient();
        
        if(hostIp == null) throw new ArgumentNullException(nameof(hostIp));
        
        var request = new RestRequest($"/Hosts/Find");
        request.AddParameter("ip", hostIp);
        
        try
        {
            
            var response = await client.GetAsync<Host>(request);

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

    public async void UpdateAsync(Host host)
    {
        using var client = MutatingClient();
        
        var request = new RestRequest($"/Hosts/{host.Id}");
        
        try
        {
            request.AddJsonBody(host);
            
            var response = await client.PutAsync(request);

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

    public async void Delete(int hostId)
    {
        using var client = MutatingClient();
        
        var request = new RestRequest($"/Hosts/{hostId}");
        
        try
        {
            
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting host");
                throw new InvalidHttpRequestException("Error deleting host", $"/Hosts/{hostId}", "DELETE");
            }
          
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting host message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting host", ex);
        }
    }

    public HostsService GetHostService(int hostId, int serviceId)
    {

        using var client = RestService.GetClient();
        
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

    public List<HostsService> GetAllHostService(int hostId)
    {
        return AsyncHelper.RunSync(() => GetAllHostServiceAsync(hostId));
    }

    public async Task<List<HostsService>> GetAllHostServiceAsync(int hostId)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services");
        
        try
        {
            var response = await client.GetAsync<List<HostsService>>(request);            
            
            if (response == null )
            {
                Logger.Error("Error getting host services");
                throw new InvalidHttpRequestException("Error getting host services", $"/Hosts/{hostId}/Services", "GET");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting host services message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting host services", ex);
        }
        
    }

    public List<Vulnerability> GetAllHostVulnerabilities(int hostId)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Vulnerabilities");
        
        try
        {
            var response = client.Get<List<Vulnerability>>(request);            
            

            if (response == null )
            {
                Logger.Error("Error getting host Vulnerabilities");
                throw new InvalidHttpRequestException("Error getting host Vulnerabilities", $"/Hosts/{hostId}/Vulnerabilities", "GET");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting host Vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting host Vulnerabilities", ex);
        }
    }

    public async Task<List<Vulnerability>> GetAllHostVulnerabilitiesAsync(int hostId)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Vulnerabilities");
        
        try
        {
            var response = await client.GetAsync<List<Vulnerability>>(request);            
            

            if (response == null )
            {
                Logger.Error("Error getting host Vulnerabilities");
                throw new InvalidHttpRequestException("Error getting host Vulnerabilities", $"/Hosts/{hostId}/Vulnerabilities", "GET");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting host Vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting host Vulnerabilities", ex);
        }
    }

    public async Task<bool> HostHasServiceAsync(int hostId, string name, int? port, string protocol)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/Exists");
        request.AddParameter("name", name);
        if(port != null) request.AddParameter("port", port.Value);
        request.AddParameter("protocol", protocol);
        
        try
        {
            var response = await client.GetAsync<bool>(request);            
            
            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error verifying service message:{Message}", ex.Message);
            throw new RestComunicationException("Error verifying host service", ex);
        }
    }

    public async Task<HostsService> CreateAndAddServiceAsync(int hostId, HostsServiceDto service)
    {
        //var client = RestService.GetClient();
        using var client = MutatingClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services");
        request.AddJsonBody(service);
        
        try
        {
            var response = await client.PostAsync<HostsService>(request);            
            

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
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/{serviceId}");

        
        try
        {
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK)
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
        using var client = RestService.GetClient();
        
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

    public async Task<HostsService?> FindServiceAsync(int hostId, string name, int? port, string protocol)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Hosts/{hostId}/Services/Find");
        request.AddParameter("name", name);
        if(port != null) request.AddParameter("port", port.Value);
        request.AddParameter("protocol", protocol);
        
        try
        {
            var response = await client.GetAsync<HostsService>(request);            
            
            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error verifying service message:{Message}", ex.Message);
            throw new RestComunicationException("Error verifying host service", ex);
        }
    }
}