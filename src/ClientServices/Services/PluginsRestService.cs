using System.Net;
using System.Net.Http;
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

    public async Task<PluginInstallResult> UploadPluginAsync(string filePath)
    {
        // reportErrorResponses: true, and Execute rather than Post, are both load-bearing. The
        // default client sets ThrowOnAnyError, which raises before the caller sees the response and
        // carries only "Request failed with status code 400" — and a refused package's whole value
        // to the operator is the server's sentence saying which rule it broke. A mutating client
        // because an upload is not a request to retry: the server may already have unpacked it.
        using var client = MutatingClient(reportErrorResponses: true);

        var request = new RestRequest("/Plugins/upload", Method.Post)
        {
            AlwaysMultipartFormData = true
        };

        // The API action parameter is named "file" (IFormFile? file), so the form field must match.
        request.AddFile("file", filePath);

        try
        {
            var response = await client.ExecutePostAsync<PluginInstallResult>(request);

            // A rejected package is a 400 carrying a PluginInstallResult. Reading the body before
            // looking at the status is what keeps that message from becoming "Bad Request".
            if (response.Data != null) return response.Data;

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                (response.ErrorException as HttpRequestException)?.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Nothing throws on this client, so the token has to be discarded here rather than
                // in a catch — otherwise an expired session leaves the client retrying with a dead
                // token and reporting an unexplained failure each time.
                authenticationService.DiscardAuthenticationToken();
                throw new RestComunicationException("Error uploading plugin",
                    new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
            }

            var detail = FirstNonEmpty(response.ErrorMessage, response.Content,
                $"The server answered {(int)response.StatusCode} {response.StatusCode}.");

            Logger.Error("Error uploading plugin: {Message}", detail);

            return new PluginInstallResult { Success = false, Message = detail };
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                authenticationService.DiscardAuthenticationToken();
            }
            Logger.Error("Error uploading plugin message: {Message}", ex.Message);
            throw new RestComunicationException("Error uploading plugin", ex);
        }
    }

    /// <summary>
    /// The first candidate that is not null, empty or whitespace.
    /// </summary>
    /// <remarks>
    /// A plain <c>??</c> chain is not enough here: RestSharp reports an empty <c>Content</c> as
    /// <c>""</c> rather than null, so <c>ErrorMessage ?? Content</c> on a bodiless 500 produces a
    /// message box with a title and nothing in it.
    /// </remarks>
    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

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