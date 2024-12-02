using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class IncidentResponsePlansRestService(IRestService restService)
    : RestServiceBase(restService), IIncidentResponsePlansService
{
    public async Task<List<IncidentResponsePlan>> GetAllAsync()
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/IncidentResponsePlans");
        
        try
        {
            var response = await client.GetAsync<List<IncidentResponsePlan>>(request);

            if (response == null)
            {
                Logger.Error("Error listing incident response plans");
                throw new InvalidHttpRequestException("Error listing incident response plans", "/IncidentResponsePlans", "GET");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing incident response plans message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing incident response plans", ex);
        }
    }
}