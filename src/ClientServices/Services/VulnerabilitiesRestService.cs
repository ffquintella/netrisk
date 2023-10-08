using System.Net;
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
}