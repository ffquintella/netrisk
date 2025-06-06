using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.FaceID;
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

            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting faceid service information message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting faceid service information", ex);
        }
    }

    public async Task SetUserEnabledStatusAsync(int userId, bool enabled)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/enable/{userId}");
        
        if(!enabled) request.Resource = $"/FaceID/disable/{userId}";

        try
        {
            await client.GetAsync<bool>(request);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error setting faceid enable status message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting faceid enable status", ex);
        }
    }

    public async Task<string> SaveAsync(int userId, string imageData, string imageType)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/save/{userId}");

        try
        {
            var faceData = new FaceData()
            {
                UserId = userId,
                ImageType = imageType,
                FaceImageB64 = imageData
            };
            
            request.AddJsonBody(faceData);
            
            var response = await client.PostAsync(request);
            
            if (response == null)
            {
                Logger.Error("Error saving faceid image message: Response is null");
                throw new RestComunicationException($"Error saving faceid image");
            }

            if (response.IsSuccessStatusCode)
            {
                return response.Content ?? string.Empty;
            }
            else
            {
                Logger.Error("Error saving faceid image message: Response is null");
                throw new RestComunicationException($"Error saving faceid image");
            }
            


        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error saving faceid image message: {Message}", ex.Message);
            throw new RestComunicationException("Error saving faceid image", ex);
        }
    }
    
    public async Task<string> SaveAsync(int userId, string imageJson) {
        
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/save/{userId}");

        try
        {
            var faceData = new FaceData()
            {
                UserId = userId,
                ImageType = "SKBitmap",
                FaceImageJson = imageJson
            };
            
            request.AddJsonBody(faceData);
            
            var response = await client.PostAsync(request);
            
            if (response == null)
            {
                Logger.Error("Error saving faceid image message: Response is null");
                throw new RestComunicationException($"Error saving faceid image");
            }

            if (response.IsSuccessStatusCode)
            {
                return response.Content ?? string.Empty;
            }
            else
            {
                Logger.Error("Error saving faceid image message: Response is null");
                throw new RestComunicationException($"Error saving faceid image");
            }
            


        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error saving faceid image message: {Message}", ex.Message);
            throw new RestComunicationException("Error saving faceid image", ex);
        }
    }

    public async Task<bool> UserHasFaceSetAsync(int userId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/faceSet/{userId}");

        try
        {
            return await client.GetAsync<bool>(request);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting faceid face set status message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting faceid face set status", ex);
        }
    }

    public async Task<FaceTransactionData?> GetFaceTransactionDataAsync(int userId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/FaceID/transactions/{userId}/start");

        try
        {
            var data = await client.GetAsync<FaceTransactionData>(request);
            
            return data;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting transaction data message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting transaction data", ex);
        }
    }
}