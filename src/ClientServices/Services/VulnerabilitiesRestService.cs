using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class VulnerabilitiesRestService: RestServiceBase, IVulnerabilitiesService
{
    public VulnerabilitiesRestService(IRestService restService) : base(restService)
    {
    }

    public List<Vulnerability> GetAll()
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities");
        try
        {
            var response = client.Get<List<Vulnerability>>(request);

            if (response == null)
            {
                Logger.Error("Error listing vulnerabilities");
                throw new InvalidHttpRequestException("Error listing vulnerabilities", "/Vulnerabilities", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing vulnerabilities message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerabilities", ex);
        }
    }

    public Vulnerability GetOne(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}");

        request.AddParameter("includeDetails", "true");
        
        try
        {
            var response = client.Get<Vulnerability>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerability");
                throw new InvalidHttpRequestException("Error getting vulnerability", $"/Vulnerabilities/{id}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting vulnerability message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting vulnerability", ex);
        } 
    }

    public List<RiskScoring> GetRisksScores(int vulnerabilityId)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerabilityId}/RisksScores");

        
        try
        {
            var response = client.Get<List<RiskScoring>>(request);

            if (response == null)
            {
                Logger.Error("Error getting vulnerability risks scores");
                throw new InvalidHttpRequestException("Error getting vulnerability risks scores", $"/Vulnerabilities/{vulnerabilityId}", "GET");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting vulnerability  risks scores message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting vulnerability  risks scores", ex);
        } 
    }

    public Vulnerability Create(Vulnerability vulnerability)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities");

        request.AddJsonBody(vulnerability);
        
        try
        {
            var response = client.Post<Vulnerability>(request);

            if (response == null)
            {
                Logger.Error("Error creating vulnerability ");
                throw new InvalidHttpRequestException("Error creating vulnerability", $"/Vulnerabilities", "POST");
            }
            
            return response;
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating vulnerability  message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating vulnerability ", ex);
        } 
    }

    public void Update(Vulnerability vulnerability)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerability.Id}");

        request.AddJsonBody(vulnerability);
        
        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating vulnerability ");
                throw new InvalidHttpRequestException("Error updating vulnerability", $"/Vulnerabilities/{vulnerability.Id}", "PUT");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability  message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        } 
    }

    public void Delete(Vulnerability vulnerability)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerability.Id}");
       
        try
        {
            var response = client.Delete(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting vulnerability ");
                throw new InvalidHttpRequestException("Error deleting vulnerability", $"/Vulnerabilities/{vulnerability.Id}", "DELETE");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating deleting  message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting vulnerability ", ex);
        } 
    }

    public void AssociateRisks(int vulnerabilityId, List<int> riskIds)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{vulnerabilityId}/RisksAssociate");

        request.AddJsonBody(riskIds);
        
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error associating vulnerability to risks");
                throw new InvalidHttpRequestException("Error associating vulnerability to risks", $"/Vulnerabilities/{vulnerabilityId}/RisksAssociate", "POST");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error associating vulnerability to risks message:{Message}", ex.Message);
            throw new RestComunicationException("Error associating vulnerability to risks", ex);
        } 
    }

    public void UpdateStatus(int id, ushort status)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}/Status");
        request.AddJsonBody(status.ToString());
       
        try
        {
            var response = client.Put(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating vulnerability status ");
                throw new InvalidHttpRequestException("Error updating vulnerability status", $"/Vulnerabilities/{id}", "PUT");
            }
            
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability status  message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        } 
    }

    public NrAction AddAction(int id, int userId, NrAction action)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Vulnerabilities/{id}/Actions");
        request.AddJsonBody(action.ToString());
       
        try
        {
            var response = client.Post(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                Logger.Error("Error updating vulnerability status ");
                throw new InvalidHttpRequestException("Error updating vulnerability status", $"/Vulnerabilities/{id}", "PUT");
            }

            var resultingAction = JsonSerializer.Deserialize<NrAction>(response.Content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return resultingAction;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating vulnerability status  message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating vulnerability ", ex);
        }
    }
}