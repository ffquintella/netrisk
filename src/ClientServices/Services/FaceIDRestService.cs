using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Services;
using RestSharp;

namespace ClientServices.Services;

public class FaceIDRestService(IRestService restService) : RestServiceBase(restService), IFaceIDService
{
    public async Task<ServiceInformation> GetInfo()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/info");

        try
        {
            var response = await client.GetAsync<ServiceInformation>(request);

            if (response == null)
            {
                Logger.Error("Error getting faceid service information");
                throw new RestComunicationException($"Error getting faceid service information");
            }

            return response;
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting faceid service information message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting faceid service information", ex);
        }
    }

    public async Task<bool> IsUserEnabledAsync(int userId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/enabled/{userId}");

        try
        {
            var response = await client.GetAsync<bool>(request);

            if (response == null)
            {
                Logger.Error("Error getting faceid enabled status");
                throw new RestComunicationException($"Error getting faceid enabled status");
            }

            return response;
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting faceid service information message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting faceid service information", ex);
        }
    }

    public Task SetUserEnabledStatusAsync(int userId, bool enabled)
    {
        throw new NotImplementedException();
    }
}