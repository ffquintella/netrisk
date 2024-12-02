using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Rest;
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

    public async Task<IncidentResponsePlan> CreateAsync(IncidentResponsePlan incidentResponsePlan)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans");
        
        try
        {
            request.AddJsonBody(incidentResponsePlan);
            
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating incident response plan ");
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error creating incident response plan", opResult!);
                
            }
                
            var newIrp = JsonSerializer.Deserialize<IncidentResponsePlan>(response.Content, new JsonSerializerOptions 
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (newIrp == null)
            {
                Logger.Error("Error creating incident response plan ");
                throw new InvalidHttpRequestException("Error creating incident response plan", "/IncidentResponsePlans", "POST");
            }

            return newIrp;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating incident response plan", ex);
        }
    }
}