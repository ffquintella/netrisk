using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class IncidentsRestService(IRestService restService) : RestServiceBase(restService), IIncidentsService
{
    
    public async Task<List<Incident>> GetAllAsync()
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents");
        try
        {
            var response = await client.GetAsync<List<Incident>>(request);

            if (response == null)
            {
                Logger.Error("Error listing incidents");
                throw new InvalidHttpRequestException("Error listing incidents", "/Incidents", "GET");
            }
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing incidents message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing incidents", ex);
        }
    }

    public async Task<List<FileListing>> GetAttachmentsAsync(int incidentId)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents/{incidentId}/Attachments");
        try
        {
            var response = await client.GetAsync<List<FileListing>>(request);

            if (response == null)
            {
                Logger.Error("Error listing incidents attachments");
                throw new InvalidHttpRequestException("Error listing incidents attachments", $"/Incidents/{incidentId}/Attachments", "GET");
            }
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing incidents attachments message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing incidents attachments", ex);
        }
    }

    public async Task<List<int>> GetIncidentResponsPlanIdsByIdAsync(int id)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents/{id}/IncidentResponsePlans");
        try
        {
            var response = await client.GetAsync<List<int>>(request);

            if (response == null)
            {
                Logger.Error("Error listing incidents response plans associated to incident");
                throw new InvalidHttpRequestException("Error listing incidents response plans associated to incident", $"/Incidents/{id}/IncidentResponsePlans", "GET");
            }
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing incidents response plans associated to incident message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing incidents response plans associated to incident", ex);
        }
    }
    
    public async Task AssociateIncidentResponsPlanIdsByIdAsync(int id, List<int> ids){
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents/{id}/IncidentResponsePlans");
        try
        {
            request.AddJsonBody(ids);
            
            var response = await client.PostAsync(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Logger.Error("Error associating incidents response plans to incident");
                throw new InvalidHttpRequestException("Error associating incidents response plans to incident", $"/Incidents/{id}/IncidentResponsePlans", "POST");
            }
            
            return ;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error associating incidents response plans to incident message:{Message}", ex.Message);
            throw new RestComunicationException("Error associating incidents response plans to incident", ex);
        }
    }

    public async Task<int> GetNextSequenceAsync(int year = -1)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents/NextSequence");
        
        request.AddQueryParameter("year", year.ToString());
        
        try
        {
            var response = await client.GetAsync<int>(request);
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting next sequence message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting next sequence", ex);
        }
    }

    public async Task<Incident> CreateAsync(Incident incident)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents");

        request.AddJsonBody(incident);
        
        try
        {
            var response = await client.PostAsync<Incident>(request);
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating incident message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating incident", ex);
        }
    }

    public async Task<Incident> UpdateAsync(Incident incident)
    {
        using var client = RestService.GetReliableClient();
        
        var request = new RestRequest($"/Incidents/{incident.Id}");

        request.AddJsonBody(incident);
        
        try
        {
            var response = await client.PutAsync<Incident>(request);
            
            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating incident message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating incident", ex);
        }
    }
    
}