using System.Net;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Plugins;
using ReliableRestClient.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class PluginsRestService(
    IRestService restService,
    IAuthenticationService authenticationService)
    : RestServiceBase(restService),  IPluginsService
{
    public async Task<List<PluginInfo>> GetPluginsAsync()
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Plugins");
        
        try
        {
            var response = await client.GetAsync<List<PluginInfo>>(request);

            if (response == null)
            {
                Logger.Error("Error getting plugins list");
                throw new RestException(500, "Error getting plugins list");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error getting all plugins message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting all plugins", ex);
        }
    }

    public async Task SetPluginEnabledAsync(string pluginName, bool enabled)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest("/Plugins");
        
        if (enabled)
        {
            request = new RestRequest($"/Plugins/enable/{pluginName}");
        }
        else
        {
            request = new RestRequest($"/Plugins/disable/{pluginName}");
        }
        
        try
        {
            await client.GetAsync<bool>(request);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error setting plugin status message: {Message}", ex.Message);
            throw new RestComunicationException("Error setting plugin status", ex);
        }
    }

    public async Task RequestPluginsReloadAsync()
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Plugins/reload");
        
        try
        {
            var response = await client.GetAsync(request);

            if (response is not { IsSuccessful: true })
            {
                Logger.Error("Error reloading plugins ");
                throw new RestException(500, "Error reloading plugins");
            }
            
            
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error reloading plugins message: {Message}", ex.Message);
            throw new RestComunicationException("Error reloading all plugins", ex);
        }
    }
}