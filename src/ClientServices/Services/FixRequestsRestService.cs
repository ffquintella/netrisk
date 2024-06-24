using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Vulnerability;
using RestSharp;

namespace ClientServices.Services;

public class FixRequestsRestService(IRestService restService) : RestServiceBase(restService), IFixRequestsService
{
    public async  Task<FixRequest> CreateFixRequestAsync(FixRequestDto fixRequest)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/FixRequest");
        try
        {

            request.AddJsonBody(fixRequest);
            
            var response = await client.PostAsync<FixRequest>(request);

            if (response == null)
            {
                Logger.Error("Error creating fix request");
                throw new RestComunicationException($"Error creating fix request");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating fix request message: {Message}", ex.Message);
            throw new RestComunicationException("Error creating fix request", ex);
        }
    }
}