using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.DTO;
using RestSharp;

namespace ClientServices.Services;

public class FixRequestsRestService(IRestService restService) : RestServiceBase(restService), IFixRequestsService
{
    public async  Task<FixRequest> CreateFixRequestAsync(FixRequestDto fixRequest, bool sendToGroup = false)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/FixRequest");

        request.AddParameter("sendToGroup", sendToGroup, ParameterType.QueryString);
        
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

    public async Task<List<FixRequest>> GetVulnerabilitiesFixRequestAsync(List<int> vulnerabilitiesIds)
    {
        using var client = RestService.GetClient();
        
        var request = new RestRequest($"/FixRequest/vulnerabilities");

        request.AddJsonBody(vulnerabilitiesIds);
        
        try
        {

            var response = await client.PostAsync<List<FixRequest>>(request);

            if (response == null)
            {
                Logger.Error("Error getting fix requests by vulnerabilities");
                throw new RestComunicationException($"Error getting fix requests by vulnerabilities");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting fix requests by vulnerabilities message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting fix requests by vulnerabilities", ex);
        }
    }
}