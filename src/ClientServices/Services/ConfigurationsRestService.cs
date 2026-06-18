using System.Net;
using System.Net.Http;
using System.Text.Json;
using ClientServices.Interfaces;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Serilog;

namespace ClientServices.Services;

public class ConfigurationsRestService: RestServiceBase, IConfigurationsService
{
    public ConfigurationsRestService(IRestService restService) : base(restService)
    {
    }

    public bool BackupPasswordIsSet()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Configurations/BackupPassword");

        try
        {
            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            
            if(response.StatusCode == HttpStatusCode.OK)
                return true;


            return false;
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error checking backup password status: {Message}", ex.Message);
            throw new RestComunicationException("checking backup password status", ex);
        }
        
    }


    public void SetBackupPassword(string password)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Configurations/BackupPassword");

        var pwd = new PasswordDto()
        {
            Password = password
        };
        
        request.AddJsonBody(pwd);
        
        
        
        try
        {
            var response = client.Put(request);


            if (response.StatusCode != HttpStatusCode.OK)
            {
                Log.Error("Error setting backup password");
                throw new RestComunicationException("Error setting backup password");
            }
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error checking backup password status: {Message}", ex.Message);
            throw new RestComunicationException("checking backup password status", ex);
        }
    }

    public WebsiteSyncConfigDto GetWebsiteSyncConfig()
    {
        var client = RestService.GetClient();
        var request = new RestRequest("/Configurations/WebsiteSync");

        try
        {
            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(response.Content))
            {
                return JsonSerializer.Deserialize<WebsiteSyncConfigDto>(response.Content!,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new WebsiteSyncConfigDto();
            }

            return new WebsiteSyncConfigDto();
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error fetching website sync config: {Message}", ex.Message);
            throw new RestComunicationException("fetching website sync config", ex);
        }
    }

    public void SetWebsiteSyncConfig(WebsiteSyncConfigDto config)
    {
        var client = RestService.GetClient();
        var request = new RestRequest("/Configurations/WebsiteSync");
        request.AddJsonBody(config);

        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Log.Error("Error setting website sync config");
                throw new RestComunicationException("Error setting website sync config");
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error setting website sync config: {Message}", ex.Message);
            throw new RestComunicationException("setting website sync config", ex);
        }
    }

}