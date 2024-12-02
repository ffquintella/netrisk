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

    public async Task<IncidentResponsePlan> UpdateAsync(IncidentResponsePlan incidentResponsePlan)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans");
        
        try
        {
            request.AddJsonBody(incidentResponsePlan);
            
            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating incident response plan ");
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error updating incident response plan", opResult!);
                
            }
                
            var newIrp = JsonSerializer.Deserialize<IncidentResponsePlan>(response.Content, new JsonSerializerOptions 
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (newIrp == null)
            {
                Logger.Error("Error updating incident response plan ");
                throw new InvalidHttpRequestException("Error updating incident response plan", "/IncidentResponsePlans", "PUT");
            }

            return newIrp;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating incident response plan", ex);
        }
    }

    public async Task DeleteAsync(int id)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{id}");

        try
        {
            
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting incident response plan ");
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error deleting incident response plan", opResult!);
                
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting incident response plan", ex);
        }
        
    }

    public async Task<IncidentResponsePlan> GetByIdAsync(int id, bool includeTasks = false)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{id}");
        try
        {
            
            if(includeTasks) request.AddQueryParameter("includeTasks", "true");
            
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting incident response plan by id ");
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting incident response plan by ip", opResult!);
            }
            
            var irp = JsonSerializer.Deserialize<IncidentResponsePlan>(response.Content!, new JsonSerializerOptions 
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (irp == null)
            {
                Logger.Error("Error getting incident response plan ");
                throw new InvalidHttpRequestException("Error getting incident response plan", "/IncidentResponsePlans", "PUT");
            }

            return irp;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting incident response plan by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting incident response plan by id", ex);
        }
    }

    public async Task<IncidentResponsePlanTask> CreateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{incidentResponsePlanTask.PlanId}/Tasks");
        try
        {
            request.AddJsonBody(incidentResponsePlanTask);
            
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating task for incident response plan ");
                    
                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error creating task for incident response plan", opResult!);
                
            }
                
            var newTask = JsonSerializer.Deserialize<IncidentResponsePlanTask>(response.Content, new JsonSerializerOptions 
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (newTask == null)
            {
                Logger.Error("Error creating task for incident response plan ");
                throw new InvalidHttpRequestException("Error creating task for incident response plan", "/IncidentResponsePlans", "POST");
            }

            return newTask;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating task for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating task for incident response plan", ex);
        }
    }
}